using System.Threading;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender;

public sealed partial class HtmlPage
{
    // A page for the same document under a new cascade (another hover, focus or active element), keeping the host's scroll positions.
    internal HtmlPage Rebuild(DocumentStyles styles, CancellationToken cancellationToken) =>
        Build(Document, styles, Options, Scroll, cancellationToken, _updater is not null);

    /// <summary>Whether this page can take a restyle in place (it was made for an interactive page).</summary>
    internal bool CanRestyleInPlace => _updater is not null;

    /// <summary>
    /// Restyles the page for a new view of the cascade without building it again: the styled nodes of the elements whose style differs
    /// are recomputed (with inheritance carried down while values change), the boxes are patched and only the subtrees that changed are
    /// laid out. Equal to <see cref="Rebuild"/> in everything the page holds.
    /// </summary>
    /// <param name="styles">The new view.</param>
    /// <param name="all">True to check every element (the view is not derived from the one the page shows).</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>False when the change reaches which nodes and boxes exist (a <c>display: none</c> that came or went, generated content, list markers); the page is then stale and the caller builds a new one with <see cref="Rebuild"/>.</returns>
    internal bool TryRestyle(DocumentStyles styles, bool all, CancellationToken cancellationToken)
    {
        if (_updater is null || _front is null || styles.HasInteractivePseudoElementRules)
        {
            return false;
        }

        StyledTree st = Styled;
        _front.CurrentStyles = styles;
        if (all)
        {
            for (int i = 0; i < st.Count; i++)
            {
                st.MarkStyleDirty(i);
            }
        }
        else
        {
            foreach (int index in styles.ChangedElements)
            {
                StyledNode n = st.FindBySource(index);
                if (!n.IsNull)
                {
                    st.MarkStyleDirty(n.Index);
                }
            }
        }

        _updater.Restyle();
        for (int k = 0; k < st.PendingCount; k++)
        {
            if ((st.PendingKinds(k) & NodeChanges.Style) == 0)
            {
                continue;
            }

            int node = st.PendingNode(k);
            DisplayType before = st.Styles[st.OldStyleOf(node)].Display, after = st.Styles[st.StyleOf(node)].Display;
            if (before.IsNone != after.IsNone || before.IsListItem != after.IsListItem || before.IsContents != after.IsContents)
            {
                return false;
            }
        }

        _updater.UpdateBoxesAndLayout(cancellationToken);
        return true;
    }
}
