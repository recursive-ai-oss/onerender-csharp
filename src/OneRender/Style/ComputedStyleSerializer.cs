using OneRender.Layout;
using OneRender.Tree;

namespace OneRender.Style;

/// <summary>Serializes the computed value of a property of a styled node the way <c>getComputedStyle()</c> does.</summary>
public static class ComputedStyleSerializer
{
    /// <summary>The serialization of one property of a node's computed style.</summary>
    /// <param name="node">A node of a styled tree.</param>
    /// <param name="property">A longhand property name in lower case, such as <c>background-color</c>.</param>
    /// <returns>The value as text, or null when the engine does not compute that property or the node is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
    public static string? Get(StyledNode node, string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return node.IsNull ? null : ComputedValueText.Get(property, in node.Values);
    }

    /// <summary>
    /// Like <see cref="Get(StyledNode, string)"/>, but for a node that has a box, <c>width</c> and <c>height</c> are the used
    /// values in px, which is what <c>getComputedStyle()</c> reports for a rendered element (CSSOM §7.1.2, resolved values); an
    /// inline box that is not replaced keeps its computed <c>auto</c>.
    /// </summary>
    /// <param name="node">A node of a styled tree.</param>
    /// <param name="property">A longhand property name in lower case.</param>
    /// <param name="fragment">The first fragment of the node's box, or the null fragment.</param>
    /// <returns>The value as text, or null when the engine does not compute that property or the node is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
    public static string? Get(StyledNode node, string property, Fragment fragment)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (node.IsNull)
        {
            return null;
        }

        bool isWidth = string.Equals(property, "width", StringComparison.Ordinal);
        if ((isWidth || string.Equals(property, "height", StringComparison.Ordinal)) && !fragment.IsNull && fragment.Kind == FragmentKind.Box && fragment.Box.Kind != BoxKind.Inline)
        {
            Rect bb = fragment.BorderBox;
            Insets border = fragment.Border;
            Insets padding = fragment.Padding;
            double used = isWidth ? bb.Width : bb.Height;
            if (node.Values.BoxSizing != BoxSizingKind.BorderBox)
            {
                used -= isWidth ? border.Horizontal + padding.Horizontal : border.Vertical + padding.Vertical;
            }

            return CssText.Px(Math.Max(0, used));
        }

        return ComputedValueText.Get(property, in node.Values);
    }
}
