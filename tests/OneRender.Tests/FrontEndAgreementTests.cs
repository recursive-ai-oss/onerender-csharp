using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace OneRender.Tests;

/// <summary>The OneHtml front end and the C# builder front end feed the same pipeline and must agree.</summary>
public class FrontEndAgreementTests
{
    private const string Html =
        "<html style=\"display:block\"><head style=\"display:none\"><title>t</title></head>" +
        "<body style=\"display:block; margin:8px; font-size:20px\">" +
        "<div style=\"display:block; width:50%; color:red; padding:1em 2em\">Hello <span style=\"display:inline; font-weight:bold\">world</span><p style=\"display:block; margin:0 auto\">para</p> tail</div>" +
        "<ul style=\"display:block; list-style-type:square\"><li style=\"display:list-item\">one</li><li style=\"display:list-item; list-style-position:inside\">two<br>three</li></ul>" +
        "<span style=\"display:inline-block; height:calc(100% - 5px)\"><img src=\"a.png\" style=\"width:10px\"><i style=\"display:contents\">c<b>d</b></i></span>" +
        "<div style=\"display:flex\">flex text<em style=\"display:inline\">item</em></div>" +
        "<div style=\"display:none\">hidden</div>" +
        "</body></html>";

    private static Pipeline FromHtml() => Pipeline.FromHtml(Html, userAgentSheet: false);

    private static StyleDeclaration D(string css)
    {
        var d = new StyleDeclaration();
        foreach (string part in css.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            Assert.True(d.TrySet(part[..colon].Trim(), part[(colon + 1)..].Trim()));
        }

        return d;
    }

    private static StyledTree FromBuilder()
    {
        var tree = new StyledTree();
        tree.Styles.SetViewport(800, 600);
        StyledNode html = tree.AddRoot("html", D("display:block"));
        StyledNode head = tree.AddElement(html, "head", D("display:none"));
        _ = head; // display:none: the title below it is not built, as in the HTML front end
        StyledNode body = tree.AddElement(html, "body", D("display:block; margin:8px; font-size:20px"));

        StyledNode div = tree.AddElement(body, "div", D("display:block; width:50%; color:red; padding:1em 2em"));
        tree.AddText(div, "Hello ");
        StyledNode span = tree.AddElement(div, "span", D("display:inline; font-weight:bold"));
        tree.AddText(span, "world");
        StyledNode p = tree.AddElement(div, "p", D("display:block; margin:0 auto"));
        tree.AddText(p, "para");
        tree.AddText(div, " tail");

        StyledNode ul = tree.AddElement(body, "ul", D("display:block; list-style-type:square"));
        StyledNode li1 = tree.AddElement(ul, "li", D("display:list-item"));
        tree.AddText(li1, "one");
        StyledNode li2 = tree.AddElement(ul, "li", D("display:list-item; list-style-position:inside"));
        tree.AddText(li2, "two");
        tree.AddElement(li2, "br");
        tree.AddText(li2, "three");

        StyledNode ib = tree.AddElement(body, "span", D("display:inline-block; height:calc(100% - 5px)"));
        tree.AddElement(ib, "img", D("width:10px"));
        StyledNode contents = tree.AddElement(ib, "i", D("display:contents"));
        tree.AddText(contents, "c");
        StyledNode bold = tree.AddElement(contents, "b");
        tree.AddText(bold, "d");

        StyledNode flex = tree.AddElement(body, "div", D("display:flex"));
        tree.AddText(flex, "flex text");
        StyledNode em = tree.AddElement(flex, "em", D("display:inline"));
        tree.AddText(em, "item");

        StyledNode hidden = tree.AddElement(body, "div", D("display:none"));
        _ = hidden;
        return tree;
    }

    [Fact]
    public void StyledTreesAreIdentical()
    {
        Pipeline html = FromHtml();
        StyledTree built = FromBuilder();
        // The HTML tree also holds text under display:none only in the DOM, not in the styled tree; the title and "hidden" are absent from both.
        Assert.Equal(built.Dump(), html.Styled.Dump());
    }

    [Fact]
    public void BoxTreesAreIdentical()
    {
        Pipeline html = FromHtml();
        StyledTree built = FromBuilder();
        var boxes = new BoxTree(built);
        boxes.Build();
        Assert.Equal(boxes.Dump(), html.Boxes.Dump());
        Assert.Contains("inline-block span", boxes.Dump(), StringComparison.Ordinal);
        Assert.Contains("block anon", boxes.Dump(), StringComparison.Ordinal);
        Assert.Contains("marker (outside)", boxes.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacedElementsAndInheritanceAgree()
    {
        Pipeline html = FromHtml();
        StyledTree built = FromBuilder();
        StyledNode a = FindByTag(html.Styled, "img");
        StyledNode b = FindByTag(built, "img");
        Assert.Equal(a.Replaced, b.Replaced);
        Assert.Equal((a.NaturalWidth, a.NaturalHeight).ToString(), (b.NaturalWidth, b.NaturalHeight).ToString());
        Assert.Equal(20, a.Values.FontSize);
        Assert.Equal(20, b.Values.FontSize);
    }

    private static StyledNode FindByTag(StyledTree tree, string tag)
    {
        for (int i = 0; i < tree.Count; i++)
        {
            StyledNode n = tree.Make(i);
            if (n.Kind == StyledNodeKind.Element && n.TagName == tag)
            {
                return n;
            }
        }

        return default;
    }

    [Fact]
    public void HtmlFrontEndMapsSourceNodesBothWays()
    {
        Pipeline p = FromHtml();
        Node div = p.Document.Root.GetElementsByTagName("div")[0];
        StyledNode s = p.Styled.FindBySource(div.Index);
        Assert.False(s.IsNull);
        Assert.Equal("div", s.TagName);
        Assert.Equal(div.Index, s.SourceIndex);
        Assert.Same(p.Document, p.Styled.SourceDocument);
        Assert.True(p.Styled.FindBySource(-1).IsNull);
        Assert.True(p.Styled.FindBySource(int.MaxValue).IsNull);
    }

    [Fact]
    public void BuilderRejectsMisuse()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("html");
        Assert.Throws<InvalidOperationException>(() => tree.AddRoot("html"));
        StyledNode text = tree.AddText(root, "x");
        Assert.Throws<ArgumentException>(() => tree.AddElement(text, "b"));
        var other = new StyledTree();
        Assert.Throws<ArgumentException>(() => other.AddElement(root, "b"));
        Assert.True(tree.AddElement(default, "b").IsNull);
        Assert.True(tree.AddText(root, string.Empty).IsNull);
        StyledNode img = tree.AddElement(root, "img");
        Assert.True(tree.AddElement(img, "b").IsNull); // replaced elements take no children
        var decl = new StyleDeclaration();
        Assert.False(decl.TrySet("no-such-property", "1"));
        Assert.False(decl.TrySet("width", string.Empty));
        Assert.False(decl.TrySet("margin", "1px 2px 3px 4px 5px"));
        Assert.False(decl.TrySet("width", new string('1', 70_000)));
    }

    [Fact]
    public void StyleContextOfTheCascadeReachesTheStyledTree()
    {
        // The cascade runs under the host's StyleContext: a hover rule only applies when the context says so.
        Document doc = Document.Parse("<html><head><style>a:hover{width:99px}</style></head><body><a id=l>x</a></body></html>");
        Node link = doc.GetElementById("l");
        double Width(StyleContext context)
        {
            DocumentStyles styles = DocumentStyles.Create(doc, context);
            var tree = new StyledTree();
            new HtmlFrontEnd(tree).Build(styles);
            return tree.FindBySource(link.Index).Values.Width.Value;
        }

        Assert.Equal(0, Width(new StyleContext()));
        Assert.Equal(99, Width(new StyleContext().WithHover(link)));
    }
}
