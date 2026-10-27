using System.Threading;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender;

public sealed partial class HtmlPage
{
    // A page for the same document under a new cascade (another hover, focus or active element), keeping the host's scroll positions.
    internal HtmlPage Rebuild(DocumentStyles styles, CancellationToken cancellationToken, double now = 0) =>
        Build(Document, styles, Options, Scroll, null, cancellationToken, _updater is not null, Motion, Styled, now, double.NaN, Form);

    internal PipelineUpdater? Updater => _updater;

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
    /// <param name="now">The animation clock, in the host's ms, for transitions and animations the change starts.</param>
    /// <returns>False when the change reaches which nodes and boxes exist (a <c>display: none</c> that came or went, generated content, list markers); the page is then stale and the caller builds a new one with <see cref="Rebuild"/>.</returns>
    internal bool TryRestyle(DocumentStyles styles, bool all, CancellationToken cancellationToken, double now = 0)
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

        // Transitions and animations see the restyled elements before boxes are touched. When any of them has effects, its style row now
        // differs in content from the recorded old style (rows are rewritten under their ids), so the boxes are generated again rather
        // than compared; the walk over the styles and the cascade are still skipped.
        if (Motion is { } motion && motion.RecalcPending(st, now, styles))
        {
            _updater.InvalidateBoxes();
        }

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

    /// <summary>
    /// Brings the page up to date after the form state of some controls changed (a checkbox toggled, a range moved, an option selected):
    /// their styles are recomputed, the text they show is refreshed, and only what changed is laid out and painted again.
    /// </summary>
    /// <returns>False when the page cannot take the change in place and must be built again with <see cref="Rebuild"/>.</returns>
    internal bool TryApplyFormChange(ReadOnlySpan<Node> controls, CancellationToken cancellationToken)
    {
        if (_updater is null || _front is null)
        {
            return false;
        }

        StyledTree st = Styled;
        foreach (Node control in controls)
        {
            StyledNode n = st.FindBySource(control.Index);
            if (n.IsNull)
            {
                continue;
            }

            st.MarkStyleDirty(n.Index);
            _front.RefreshContent(control, n.Index);
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
