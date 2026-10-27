using System.Threading;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender;

public sealed partial class HtmlPage
{
    // A page for the same document under a new cascade (another hover, focus or active element), keeping the host's scroll positions.
    internal HtmlPage Rebuild(DocumentStyles styles, CancellationToken cancellationToken, double now = 0) =>
        Build(Document, styles, Options, Scroll, cancellationToken, Motion, Styled, now);
}
