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
        Build(Document, styles, Options, Scroll, null, cancellationToken, _updater is not null, Motion, Styled, now, double.NaN, Form, TextOverrides);

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
    /// <param name="edit">True when the cascade changed because an element's classes or text were edited, not because of an interaction state; pages with generated-content rules are then built again.</param>
    /// <param name="textElement">The node index of an element whose single text child takes <paramref name="text"/> in the same pass, or -1.</param>
    /// <param name="text">The new text for <paramref name="textElement"/>.</param>
    /// <returns>False when the change reaches which nodes and boxes exist (a <c>display: none</c> that came or went, generated content, list markers); the page is then stale and the caller builds a new one with <see cref="Rebuild"/>.</returns>
    internal bool TryRestyle(DocumentStyles styles, bool all, CancellationToken cancellationToken, double now = 0, bool edit = false, int textElement = -1, string? text = null)
    {
        if (_updater is null || _front is null || styles.HasInteractivePseudoElementRules)
        {
            return false;
        }

        // Whether a ::before, ::after or ::marker box exists, and the text it shows, are fixed when the boxes are made; an edit can change them.
        if (edit && UsesGeneratedContentRules(styles))
        {
            return false;
        }

        StyledTree st = Styled;
        _front.CurrentStyles = styles;
        if (all)
        {
            for (int i = 0; i < st.Count; i++)
            {
                if (!st.IsFreeSlot(i))
                {
                    st.MarkStyleDirty(i);
                }
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

        // An edit of an element's text: its one text child takes the new characters (the styles above already know the edited snapshot).
        if (textElement >= 0 && !ApplyElementText(st, textElement, text ?? string.Empty))
        {
            return false;
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

    /// <summary>
    /// Puts the text of an edited control on screen: its text nodes are updated and only what the new text changes is laid out again.
    /// </summary>
    /// <returns>False when the page cannot take the change in place.</returns>
    internal bool ApplyTextEdit(Node control, Forms.TextEditSession session, CancellationToken cancellationToken)
    {
        if (_updater is null || _front is null)
        {
            return false;
        }

        StyledNode n = Styled.FindBySource(control.Index);
        if (n.IsNull)
        {
            return false;
        }

        Forms.TextControls.Sync(Styled, n, session.Display, session.Placeholder);
        _updater.Restyle();
        _updater.UpdateBoxesAndLayout(cancellationToken);
        return true;
    }

    /// <summary>
    /// Puts new text into an element whose styled children are one text node and lays out only what that changes. The cascade does not
    /// depend on the text, so nothing is restyled.
    /// </summary>
    /// <returns>False when the element is not of that shape (the caller builds the page again, which reads the text from the overrides).</returns>
    internal bool TryApplyText(int sourceIndex, string text, CancellationToken cancellationToken)
    {
        if (_updater is null || _front is null || !ApplyElementText(Styled, sourceIndex, text))
        {
            return false;
        }

        _updater.Restyle();
        _updater.UpdateBoxesAndLayout(cancellationToken);
        return true;
    }

    // The element's styled children are exactly one text node: the common label. Anything else (generated content, controls, wrappers,
    // elements that were not rendered) is left to a rebuild from the edited snapshot, which reads the text from it.
    private static bool ApplyElementText(StyledTree st, int sourceIndex, string text)
    {
        StyledNode element = st.FindBySource(sourceIndex);
        if (element.IsNull || element.Kind != StyledNodeKind.Element || element.Replaced != ReplacedKind.None)
        {
            return false;
        }

        StyledNode child = element.FirstChild;
        if (child.IsNull || child.Kind != StyledNodeKind.Text || !child.NextSibling.IsNull)
        {
            return false;
        }

        st.SetText(child, text);
        return true;
    }

    private System.Collections.Generic.IReadOnlyList<StyleSheetInfo>? _scannedSheets;
    private bool _generatedRules;

    // Whether an author sheet has rules for generated-content pseudo-elements (scanned once per set of sheets).
    private bool UsesGeneratedContentRules(DocumentStyles styles)
    {
        if (!ReferenceEquals(_scannedSheets, styles.Sheets))
        {
            _scannedSheets = styles.Sheets;
            _generatedRules = false;
            foreach (StyleSheetInfo info in styles.Sheets)
            {
                if (info.Origin != StyleOrigin.Author)
                {
                    continue;
                }

                string t = info.Sheet.Text;
                if (t.Contains("before", StringComparison.OrdinalIgnoreCase) || t.Contains("after", StringComparison.OrdinalIgnoreCase) || t.Contains("marker", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("counter", StringComparison.OrdinalIgnoreCase) || t.Contains("content", StringComparison.OrdinalIgnoreCase))
                {
                    _generatedRules = true;
                    break;
                }
            }
        }

        return _generatedRules;
    }
}
