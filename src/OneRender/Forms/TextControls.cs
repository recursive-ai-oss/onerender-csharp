using OneRender.Tree;

namespace OneRender.Forms;

/// <summary>Puts the text of an editable control into its styled-tree nodes: the value node first, then the optional placeholder node.</summary>
internal static class TextControls
{
    /// <summary>The control's value node (its first text child), or the null node.</summary>
    public static StyledNode ValueNode(StyledNode control)
    {
        for (StyledNode c = control.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == StyledNodeKind.Text)
            {
                return c;
            }
        }

        return default;
    }

    /// <summary>Writes <paramref name="display"/> into the value node and shows the placeholder only while it is empty.</summary>
    public static void Sync(StyledTree tree, StyledNode control, ReadOnlySpan<char> display, string placeholder)
    {
        StyledNode value = ValueNode(control);
        if (value.IsNull)
        {
            return;
        }

        tree.SetText(value, display);
        for (StyledNode c = value.NextSibling; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == StyledNodeKind.Text)
            {
                tree.SetText(c, display.IsEmpty ? placeholder : default);
                return;
            }
        }
    }
}
