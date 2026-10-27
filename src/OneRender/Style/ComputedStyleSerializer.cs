using System.Globalization;
using System.Text;
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
    /// The resolved value of a property (CSSOM §7.1.2), which is what <c>getComputedStyle()</c> returns: the computed value, except
    /// that for a node with a box the properties that resolve to the used value are given as the used px (<c>width</c>,
    /// <c>height</c>, <c>padding-*</c> and <c>margin-*</c> that are percentages or <c>auto</c>), <c>currentcolor</c> is replaced by
    /// the color, a unitless <c>line-height</c> is multiplied out, <c>min-width</c> and <c>min-height</c> of <c>auto</c> are
    /// <c>0px</c> outside flex and grid containers, <c>word-spacing</c> of <c>normal</c> is <c>0px</c>, an unused <c>outline-width</c>
    /// is the medium width, and numbers have at most six significant digits.
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

        string? value = ComputedValueText.Get(property, in node.Values);
        if (value is null)
        {
            return null;
        }

        bool box = !fragment.IsNull && fragment.Kind == FragmentKind.Box;
        switch (property)
        {
            case "width":
            case "height":
                if (box && fragment.Box.Kind != BoxKind.Inline)
                {
                    bool isWidth = property[0] == 'w';
                    Rect bb = fragment.BorderBox;
                    double used = isWidth ? bb.Width : bb.Height;
                    if (node.Values.BoxSizing != BoxSizingKind.BorderBox)
                    {
                        Insets border = fragment.Border;
                        Insets padding = fragment.Padding;
                        used -= isWidth ? border.Horizontal + padding.Horizontal : border.Vertical + padding.Vertical;
                    }

                    return Number(Math.Max(0, used)) + "px";
                }

                break;
            case "padding-top":
                return box && !node.Values.PaddingTop.IsPx ? Number(fragment.Padding.Top) + "px" : Numbers(value);
            case "padding-right":
                return box && !node.Values.PaddingRight.IsPx ? Number(fragment.Padding.Right) + "px" : Numbers(value);
            case "padding-bottom":
                return box && !node.Values.PaddingBottom.IsPx ? Number(fragment.Padding.Bottom) + "px" : Numbers(value);
            case "padding-left":
                return box && !node.Values.PaddingLeft.IsPx ? Number(fragment.Padding.Left) + "px" : Numbers(value);
            case "margin-left":
            case "margin-right":
                return box && !(property[7] == 'l' ? node.Values.MarginLeft : node.Values.MarginRight).IsPx ? UsedSideMargin(node, fragment, property[7] == 'l') ?? value : Numbers(value);
            case "margin-top":
            case "margin-bottom":
                if (box && (property[7] == 't' ? node.Values.MarginTop : node.Values.MarginBottom) is { IsPx: false } m)
                {
                    return m.Kind == LengthKind.Auto ? "0px" : (UsedPercentMargin(node, fragment, m) ?? value);
                }

                break;
            case "min-width":
            case "min-height":
                if (string.Equals(value, "auto", StringComparison.Ordinal) && !IsFlexOrGridItem(node))
                {
                    return "0px";
                }

                break;
            case "word-spacing":
                if (string.Equals(value, "normal", StringComparison.Ordinal))
                {
                    return "0px";
                }

                break;
            case "outline-width":
                if (string.Equals(value, "0px", StringComparison.Ordinal) && node.Values.OutlineStyle == BorderStyleKind.None)
                {
                    return "3px";
                }

                break;
            case "line-height":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor))
                {
                    return Number(factor * node.Values.FontSize) + "px";
                }

                break;
        }

        if (string.Equals(value, "currentcolor", StringComparison.Ordinal))
        {
            return node.Values.Color.ToCss();
        }

        return Numbers(value);
    }

    private static bool IsFlexOrGridItem(StyledNode node)
    {
        StyledNode parent = node.Parent;
        return !parent.IsNull && parent.Values.Display.Inside is InnerDisplay.Flex or InnerDisplay.Grid;
    }

    // The distance from the containing block's content edge to the box's margin edge, for a block-level box in normal flow whose
    // parent is a block container: what an auto or percentage margin came out as.
    private static string? UsedSideMargin(StyledNode node, Fragment fragment, bool left)
    {
        Fragment parent = fragment.Parent;
        if (parent.IsNull || parent.Kind != FragmentKind.Box || fragment.Box.Level != BoxLevel.Block || node.Values.Position is PositionKind.Absolute or PositionKind.Fixed || node.Values.Floating != FloatKind.None)
        {
            return null;
        }

        Insets pb = parent.Border;
        Insets pp = parent.Padding;
        Rect bb = fragment.BorderBox;
        double contentLeft = pb.Left + pp.Left;
        double contentRight = parent.BorderBox.Width - pb.Right - pp.Right;
        return Number(left ? bb.X - contentLeft : contentRight - bb.Right) + "px";
    }

    private static string? UsedPercentMargin(StyledNode node, Fragment fragment, Length margin)
    {
        _ = node;
        Fragment parent = fragment.Parent;
        if (margin.Kind != LengthKind.Percent || parent.IsNull || parent.Kind != FragmentKind.Box)
        {
            return null;
        }

        double basis = parent.BorderBox.Width - parent.Border.Horizontal - parent.Padding.Horizontal;
        return Number(margin.Value / 100 * basis) + "px";
    }

    private static string Number(double v) => Numbers(CssText.Number(v));

    // A browser keeps at most six significant digits of a number when it serializes a computed value.
    private static string Numbers(string text)
    {
        StringBuilder? sb = null;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (!(char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))) || (i > 0 && (char.IsAsciiLetter(text[i - 1]) || text[i - 1] == '-' && i > 1 && char.IsAsciiLetter(text[i - 2]))))
            {
                sb?.Append(c);
                i++;
                continue;
            }

            int start = i;
            bool dot = false;
            while (i < text.Length && (char.IsAsciiDigit(text[i]) || (text[i] == '.' && !dot)))
            {
                dot |= text[i] == '.';
                i++;
            }

            ReadOnlySpan<char> run = text.AsSpan(start, i - start);
            if (dot && SignificantDigits(run) > 6 && double.TryParse(run, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                sb ??= new StringBuilder(text.Length).Append(text, 0, start);
                sb.Append(CssText.Number(Math.Round(d, DecimalsFor(d), MidpointRounding.AwayFromZero)));
            }
            else
            {
                sb?.Append(run);
            }
        }

        return sb is null ? text : sb.ToString();
    }

    private static int SignificantDigits(ReadOnlySpan<char> run)
    {
        int n = 0;
        bool leading = true;
        foreach (char c in run)
        {
            if (c == '.')
            {
                continue;
            }

            if (leading && c == '0')
            {
                continue;
            }

            leading = false;
            n++;
        }

        return n;
    }

    private static int DecimalsFor(double d)
    {
        double a = Math.Abs(d);
        int integerDigits = a < 1 ? 0 : (int)Math.Floor(Math.Log10(a)) + 1;
        return Math.Max(0, 6 - integerDigits);
    }
}
