using OneRender.Style;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tree;

public sealed partial class HtmlFrontEnd : INodeStyler
{
    private DocumentStyles? _current;

    /// <summary>
    /// Makes the cascade that later restyles read: after <see cref="Build"/> it is the one the tree was built from, and a host that
    /// restyles in place (a state change) hands the new view here before marking nodes.
    /// </summary>
    internal DocumentStyles? CurrentStyles
    {
        get => _current;
        set => _current = value;
    }

    StyleId INodeStyler.ComputeElement(StyledTree tree, int node, StyleId parentStyle)
    {
        DocumentStyles? styles = _current;
        Document? doc = tree.SourceDocument;
        int source = tree.SourceIndexOf(node);
        if (styles is null || doc is null || source < 0)
        {
            return new StyleId(tree.StyleOf(node).Index);
        }

        Node element = doc.GetNode(source);
        int parent = tree.ParentOf(node);
        StyleId resolved = Resolve(styles.GetStyle(element), parentStyle, parent >= 0, parent < 0);
        resolved = AdjustControlStyle(element, resolved, parent < 0 ? default : tree.StyleOf(parent));
        string tag = tree.TagNameOf(node);
        if (element.Namespace == ElementNamespace.Html && (tag.Equals("table", StringComparison.Ordinal) || tag.Equals("tr", StringComparison.Ordinal) || tag.Equals("td", StringComparison.Ordinal) || tag.Equals("th", StringComparison.Ordinal)))
        {
            // The presentational hints read the node's current style and may derive a new one; the tree keeps its old style until the caller compares.
            StyleId old = tree.StyleOf(node);
            tree.ReplaceStyle(node, resolved);
            ApplyTableHints(element, node, tag);
            resolved = tree.StyleOf(node);
            tree.ReplaceStyle(node, old);
        }

        return resolved;
    }

    StyleId INodeStyler.ComputeOther(StyledTree tree, int node, StyleId parentStyle)
    {
        DocumentStyles? styles = _current;
        Document? doc = tree.SourceDocument;
        int owner = tree.ParentOf(node);
        if (styles is null || doc is null || owner < 0 || tree.SourceIndexOf(owner) < 0)
        {
            return tree.StyleOf(node);
        }

        Node element = doc.GetNode(tree.SourceIndexOf(owner));
        switch ((StyledNodeKind)tree.KindOf(node))
        {
            case StyledNodeKind.Marker:
            {
                ComputedStyle? marker = styles.GetPseudoStyle(element, PseudoElementKind.Marker);
                return marker is null ? tree.Styles.GetAnonymous(parentStyle, DisplayType.Inline) : Resolve(marker, parentStyle, true, false);
            }

            case StyledNodeKind.Before:
            case StyledNodeKind.After:
            {
                var kind = (StyledNodeKind)tree.KindOf(node) == StyledNodeKind.Before ? PseudoElementKind.Before : PseudoElementKind.After;
                ComputedStyle? ps = styles.GetPseudoStyle(element, kind);
                return ps is null ? tree.StyleOf(node) : Resolve(ps, parentStyle, true, false);
            }

            default:
                return tree.StyleOf(node);
        }
    }
}
