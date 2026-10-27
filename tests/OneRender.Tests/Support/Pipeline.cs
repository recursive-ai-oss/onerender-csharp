using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tests.Support;

/// <summary>Builds the whole front half of the pipeline from an HTML string.</summary>
internal sealed class Pipeline
{
    private Pipeline(Document document, DocumentStyles styles, StyledTree styled, BoxTree boxes)
    {
        Document = document;
        Styles = styles;
        Styled = styled;
        Boxes = boxes;
    }

    public Document Document { get; }

    public DocumentStyles Styles { get; }

    public StyledTree Styled { get; }

    public BoxTree Boxes { get; }

    public static Pipeline FromHtml(string html, double viewportWidth = 800, double viewportHeight = 600, bool userAgentSheet = true, IResourceResolver? resolver = null)
    {
        Document doc = Document.Parse(html);
        var context = new StyleContext { Media = new MediaFeatures { Width = viewportWidth, Height = viewportHeight } };
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions { IncludeUserAgentSheet = userAgentSheet });
        var table = new OneRender.Style.StyleTable();
        table.SetViewport(viewportWidth, viewportHeight);
        var styled = new StyledTree(table) { Resolver = resolver };
        new HtmlFrontEnd(styled).Build(styles);
        var boxes = new BoxTree(styled);
        boxes.Build();
        return new Pipeline(doc, styles, styled, boxes);
    }

    /// <summary>The box dump of an HTML body, without html/body wrappers: pass the content of body.</summary>
    public static string BodyDump(string bodyHtml, bool userAgentSheet = false)
    {
        Pipeline p = FromHtml("<!DOCTYPE html><html><body>" + bodyHtml + "</body></html>", userAgentSheet: userAgentSheet);
        return p.Boxes.Dump();
    }
}
