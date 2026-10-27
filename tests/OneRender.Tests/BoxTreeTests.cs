using System.Linq;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Box generation: CSS Display 3 §2 and CSS2 §9.2, checked on the box-tree dump.</summary>
public class BoxTreeTests
{
    /// <summary>Dumps the boxes under body, dropping the html and body lines and their indent.</summary>
    private static string Dump(string bodyHtml, string head = "")
    {
        Pipeline p = Pipeline.FromHtml("<!DOCTYPE html><html><head>" + head + "</head><body>" + bodyHtml + "</body></html>");
        string[] lines = p.Boxes.Dump().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("block html", lines[0]);
        Assert.Equal("  block body", lines[1]);
        return string.Join("\n", lines.Skip(2).Select(l => l[4..])) + "\n";
    }

    [Fact]
    public void TextInABlockIsAnAnonymousInlineBox()
    {
        Assert.Equal("block div\n  text \"hello\"\n", Dump("<div>hello</div>"));
    }

    [Fact]
    public void RunsOfInlineContentBesideBlocksGetAnonymousBlocks()
    {
        // CSS2 §9.2.1.1.
        Assert.Equal(
            "block div\n  block anon\n    text \"a\"\n  block p\n    text \"b\"\n  block anon\n    text \"c\"\n",
            Dump("<div>a<p>b</p>c</div>"));
        Assert.Equal(
            "block div\n  block anon\n    text \"a\"\n    inline b\n      text \"b\"\n  block p\n    text \"c\"\n",
            Dump("<div>a<b>b</b><p>c</p></div>"));
    }

    [Fact]
    public void ABlockInsideAnInlineSplitsTheInline()
    {
        // CSS2 §9.2.1.1: the inline box is broken around the block; both pieces are wrapped in anonymous blocks.
        Assert.Equal(
            "block div\n" +
            "  block anon\n    text \"x\"\n    inline span\n      text \"a\"\n" +
            "  block p\n    text \"b\"\n" +
            "  block anon\n    inline span (continuation)\n      text \"c\"\n    text \"y\"\n",
            Dump("<div>x<span>a<p>b</p>c</span>y</div>"));
    }

    [Fact]
    public void NestedInlinesAreSplitTogether()
    {
        Assert.Equal(
            "block div\n" +
            "  block anon\n    inline span\n      inline b\n        text \"x\"\n" +
            "  block p\n    text \"p\"\n" +
            "  block anon\n    inline span (continuation)\n      inline b (continuation)\n        text \"y\"\n",
            Dump("<div><span><b>x<p>p</p>y</b></span></div>"));
    }

    [Fact]
    public void EmptyPiecesOfASplitInlineAreKept()
    {
        // "even if either side is empty"
        Assert.Equal(
            "block div\n  block anon\n    inline span\n  block p\n    text \"p\"\n  block anon\n    inline span (continuation)\n",
            Dump("<div><span><p>p</p></span></div>"));
    }

    [Fact]
    public void TwoBlocksInOneInlineMakeThreePieces()
    {
        Pipeline p = Pipeline.FromHtml("<body><span id=s>a<p>1</p>b<p>2</p>c</span></body>");
        Box piece = p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("s").Index));
        int pieces = 0;
        for (Box b = piece; !b.IsNull; b = b.NextPiece)
        {
            pieces++;
            Assert.Equal(pieces > 1, (b.Flags & BoxTraits.Continuation) != 0);
        }

        Assert.Equal(3, pieces);
    }

    [Fact]
    public void FloatsAndAbsoluteBoxesDoNotSplitInlines()
    {
        Assert.Equal(
            "block div\n  text \"a\"\n  inline span\n    text \"b\"\n    block div (out-of-flow)\n      text \"f\"\n    text \"c\"\n",
            Dump("<div>a<span>b<div style='float:left'>f</div>c</span></div>"));
        Assert.Equal(
            "block div\n  inline span\n    text \"b\"\n    block i (out-of-flow)\n      text \"x\"\n",
            Dump("<div><span>b<i style='position:absolute'>x</i></span></div>"));
    }

    [Fact]
    public void FloatBesideBlocksStaysADirectChildUnlessAnInlineRunIsOpen()
    {
        Assert.Equal(
            "block div\n  block p\n    text \"a\"\n  block div (out-of-flow)\n    text \"f\"\n  block p\n    text \"b\"\n",
            Dump("<div><p>a</p><div style='float:left'>f</div><p>b</p></div>"));
        Assert.Equal(
            "block div\n  block anon\n    text \"t\"\n    block div (out-of-flow)\n      text \"f\"\n  block p\n    text \"b\"\n",
            Dump("<div>t<div style='float:left'>f</div><p>b</p></div>"));
    }

    [Fact]
    public void WhiteSpaceOnlyTextBetweenBlocksGeneratesNoBox()
    {
        // CSS2 §9.2.2.1 / CSS Text 3 §4.1.1.
        Assert.Equal(
            "block div\n  block p\n    text \"a\"\n  block p\n    text \"b\"\n",
            Dump("<div> <p>a</p>\n  <p>b</p> \n</div>"));
    }

    [Fact]
    public void WhiteSpaceAtTheEdgesOfLinesIsTrimmedButNotInTheMiddle()
    {
        Assert.Equal("block div\n  inline span\n    text \"a\"\n", Dump("<div> <span>a</span> </div>"));
        Assert.Equal(
            "block div\n  text \"a\"\n  inline span\n    text \" \"\n  text \"b\"\n",
            Dump("<div>a<span> </span>b</div>"));
        Assert.Equal("block div\n", Dump("<div>   </div>"));
        Assert.Equal("block div\n  text \"a \"\n  inline b\n    text \"c\"\n", Dump("<div>a <b>c</b></div>"));
    }

    [Fact]
    public void PreservedWhiteSpaceGeneratesBoxes()
    {
        Assert.Equal(
            "block div\n  block anon\n    text \" \"\n  block p\n    text \"a\"\n  block anon\n    text \" \"\n",
            Dump("<div style='white-space:pre'> <p>a</p> </div>"));
        // pre-line keeps line breaks, so a text with one is not collapsed away; spaces alone are
        Assert.Equal("block div\n  block anon\n    text \"\\n\"\n  block p\n    text \"a\"\n", Dump("<div style='white-space:pre-line'>\n<p>a</p>  </div>"));
    }

    [Fact]
    public void DisplayNoneGeneratesNothingAndContentsGeneratesNoBoxButPromotesChildren()
    {
        Assert.Equal("block div\n  text \"a\"\n  text \"b\"\n  inline i\n    text \"c\"\n", Dump("<div><span style='display:none'>x<b>y</b></span>a<span style='display:contents'>b<i>c</i></span></div>"));
        Assert.Equal("block div\n  block p\n    text \"1\"\n  block p\n    text \"2\"\n", Dump("<div><span style='display:contents'><p>1</p><p>2</p></span></div>"));
        // a block inside display:contents inside an inline still splits the inline
        Assert.Equal(
            "block div\n  block anon\n    inline span\n  block p\n    text \"1\"\n  block anon\n    inline span (continuation)\n      text \"z\"\n",
            Dump("<div><span><b style='display:contents'><p>1</p></b>z</span></div>"));
    }

    [Fact]
    public void ReplacedAndLineBreakBoxes()
    {
        Assert.Equal(
            "block div\n  text \"a\"\n  inline-replaced img\n  br\n  text \"b\"\n",
            Dump("<div>a<img src=x.png><br>b</div>"));
        Assert.Equal("block div\n  block-replaced img\n", Dump("<div><img style='display:block' src=x></div>"));
        Assert.Equal("block div\n  inline-replaced canvas\n", Dump("<div><canvas>fallback <b>text</b></canvas></div>"));
        // contents on a replaced element generates nothing
        Assert.Equal("block div\n", Dump("<div><img style='display:contents' src=x></div>"));
    }

    [Fact]
    public void InlineBlockFlowRootAndFormattingContexts()
    {
        Assert.Equal("block div\n  inline-block span\n    text \"a\"\n", Dump("<div><span style='display:inline-block'>a</span></div>"));
        Assert.Equal("block div\n  flow-root div\n    text \"a\"\n", Dump("<div><div style='display:flow-root'>a</div></div>"));
        Assert.Equal("block div\n  inline-flex span\n    block anon\n      text \"a\"\n    block b (blockified)\n      text \"b\"\n", Dump("<div><span style='display:inline-flex'>a<b>b</b></span></div>"));
        Assert.Equal("block div\n  grid div\n    block p\n      text \"1\"\n", Dump("<div><div style='display:grid'> <p>1</p> </div></div>"));
        // an inline-block is atomic: a block inside it does not split the line it sits in
        Assert.Equal(
            "block div\n  inline-block span\n    block p\n      text \"p\"\n",
            Dump("<div><span style='display:inline-block'><p>p</p></span></div>"));
    }

    [Fact]
    public void FlexItemsAreBlockifiedAndTextRunsBecomeAnonymousItems()
    {
        // Flexbox §4: contiguous text becomes an anonymous item, white space only is not rendered, inline children are blockified.
        Assert.Equal(
            "flex div\n  block anon\n    text \"a\"\n  block span (blockified)\n    text \"b\"\n  block p\n    text \"c\"\n",
            Dump("<div style='display:flex'>a<span>b</span> <p>c</p></div>"));
    }

    [Fact]
    public void ListItemsGetMarkerPlaceholders()
    {
        // CSS Lists 3 §3
        Assert.Equal(
            "block ul\n  block li (list-item)\n    marker (outside)\n    text \"one\"\n  block li (list-item)\n    marker\n    text \"two\"\n",
            Dump("<ul><li>one</li><li style='list-style-position:inside'>two</li></ul>"));
        Assert.Equal("block ul\n  block li (list-item)\n    text \"one\"\n", Dump("<ul><li style='list-style-type:none'>one</li></ul>"));
        Assert.Equal(
            "block li (list-item)\n  marker (outside)\n  block p\n    text \"x\"\n",
            Dump("<li><p>x</p></li>"));
        Assert.Equal(
            "block div (list-item)\n  marker (outside)\n  text \"x\"\n",
            Dump("<div style='display:list-item'>x</div>"));
    }

    [Fact]
    public void TableDisplayTypesProduceMarkedBoxes()
    {
        Assert.Equal(
            "table table\n  table-row-group tbody\n    table-row tr\n      table-cell td\n        text \"x\"\n      table-cell td\n        block p\n          text \"y\"\n",
            Dump("<table> <tr> <td>x</td><td><p>y</p></td> </tr> </table>"));
        Assert.Equal("inline-table span\n", Dump("<span style='display:inline-table'></span>"));
    }

    [Fact]
    public void PseudoElementsBecomeBoxesWithGeneratedText()
    {
        string css = "<style>p::before{content:'[';} p::after{content:']'; display:block} i::after{content:''} b::before{content:none}</style>";
        Assert.Equal(
            "block p\n  block anon\n    inline ::before\n      text \"[\"\n    text \"x\"\n  block ::after\n    text \"]\"\n",
            Dump("<p>x</p>", css));
        Assert.Equal("inline i\n  text \"x\"\n  inline ::after\n", Dump("<i>x</i>", css));
        Assert.Equal("inline b\n  text \"y\"\n", Dump("<b>y</b>", css));
    }

    [Fact]
    public void ReplacedElementsKeepNaturalSizesFromTheResolver()
    {
        var resolver = new FixedResolver();
        Pipeline p = Pipeline.FromHtml("<body><img id=a src='pic.png'><img id=b src='unknown.png'><canvas id=c width=40 height=20></canvas><iframe id=d></iframe></body>", resolver: resolver);
        StyledNode a = p.Styled.FindBySource(p.Document.GetElementById("a").Index);
        Assert.Equal(64, a.NaturalWidth);
        Assert.Equal(48, a.NaturalHeight);
        StyledNode b = p.Styled.FindBySource(p.Document.GetElementById("b").Index);
        Assert.True(double.IsNaN(b.NaturalWidth));
        StyledNode c = p.Styled.FindBySource(p.Document.GetElementById("c").Index);
        Assert.Equal((40, 20), (c.NaturalWidth, c.NaturalHeight));
        StyledNode d = p.Styled.FindBySource(p.Document.GetElementById("d").Index);
        Assert.Equal((300, 150), (d.NaturalWidth, d.NaturalHeight));
        Assert.Equal(ReplacedKind.Image, a.Replaced);
        Assert.Equal(ReplacedKind.Embedded, d.Replaced);
    }

    private sealed class FixedResolver : IResourceResolver
    {
        public bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height)
        {
            if (source.SequenceEqual("pic.png"))
            {
                width = 64;
                height = 48;
                return true;
            }

            width = height = 0;
            return false;
        }
    }

    [Fact]
    public void PrincipalBoxAndNodeMapping()
    {
        Pipeline p = Pipeline.FromHtml("<body><div id=a>x<span id=b style='display:contents'>y</span><i id=c style='display:none'></i></div></body>");
        StyledNode a = p.Styled.FindBySource(p.Document.GetElementById("a").Index);
        Box box = p.Boxes.PrincipalBox(a);
        Assert.False(box.IsNull);
        Assert.Equal(a, box.Node);
        Assert.True(p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("b").Index)).IsNull);
        Assert.True(p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("c").Index)).IsNull);
        Assert.Equal(p.Document.GetElementById("a").Index, a.SourceIndex);
        Assert.Equal(ChildLayout.Inline, box.ChildLayout);
    }

    [Fact]
    public void ChildLayoutIsSettledForEveryContainer()
    {
        Pipeline p = Pipeline.FromHtml("<body><div id=a></div><div id=b>t</div><div id=c><p>x</p></div><div id=d>t<p>x</p></div></body>");
        ChildLayout Of(string id) => p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById(id).Index)).ChildLayout;
        Assert.Equal(ChildLayout.Empty, Of("a"));
        Assert.Equal(ChildLayout.Inline, Of("b"));
        Assert.Equal(ChildLayout.Block, Of("c"));
        Assert.Equal(ChildLayout.Block, Of("d"));
    }

    [Fact]
    public void EveryContainerHasOnlyInlineOrOnlyBlockInFlowChildren()
    {
        // The invariant layout relies on, checked on a messy document.
        Pipeline p = Pipeline.FromHtml(
            "<body>a<div>b<span>c<div>d<b>e<p>f</p>g</b>h</div>i</span>j<ul><li>k<p>l</p></li><li>m</li></ul>n<img src=x><div style='float:left'>o</div>p</div>q</body>");
        for (int i = 0; i < p.Boxes.Count; i++)
        {
            var b = new Box(p.Boxes, i);
            if (b.Kind != BoxKind.Container || !Reachable(b))
            {
                continue;
            }

            bool blocks = false, inlines = false;
            for (Box c = b.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if ((c.Flags & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) != 0)
                {
                    continue;
                }

                if (c.Level == BoxLevel.Block)
                {
                    blocks = true;
                }
                else
                {
                    inlines = true;
                }
            }

            Assert.False(blocks && inlines, "mixed children under box " + i + "\n" + p.Boxes.Dump());
        }
    }

    private static bool Reachable(Box b)
    {
        while (!b.Parent.IsNull)
        {
            b = b.Parent;
        }

        return b.Index == 0;
    }
}
