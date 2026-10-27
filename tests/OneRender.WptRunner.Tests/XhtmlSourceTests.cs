namespace OneRender.WptRunner.Tests;

public class XhtmlSourceTests
{
    [Fact]
    public void CdataInStyleKeepsItsTextVerbatim()
    {
        Assert.Equal("<style>\n a > b { x: 1 }\n </style>", XhtmlSource.ToHtml("<style><![CDATA[\n a > b { x: 1 }\n ]]></style>"));
    }

    [Fact]
    public void CdataElsewhereIsEscapedText()
    {
        Assert.Equal("<p>a &lt;b&gt; &amp; c</p>", XhtmlSource.ToHtml("<p><![CDATA[a <b> & c]]></p>"));
    }

    [Fact]
    public void EmptyElementTagsExpandExceptForVoidElements()
    {
        Assert.Equal("<div class=\"a/>\"></div><br/><img src=\"x\" /><td ></td>", XhtmlSource.ToHtml("<div class=\"a/>\"/><br/><img src=\"x\" /><td />"));
    }

    [Fact]
    public void ProcessingInstructionsAreDroppedAndCommentsKept()
    {
        Assert.Equal("<!-- <![CDATA[ --><p/>".Replace("<p/>", "<p></p>"), XhtmlSource.ToHtml("<?xml version=\"1.0\"?><!-- <![CDATA[ --><p/>"));
    }

    [Fact]
    public void OnlyXhtmlExtensionsAreConverted()
    {
        Assert.True(XhtmlSource.IsXhtml("css/a.xht"));
        Assert.True(XhtmlSource.IsXhtml("css/a.XHTML"));
        Assert.False(XhtmlSource.IsXhtml("css/a.html"));
    }
}
