using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>
/// Shorthands the OneHtml cascade keeps as one token list (it has no grammar for them) but whose longhands the engine
/// computes: <c>white-space</c>, <c>border-radius</c> and <c>overflow</c>. Both front ends expand them here.
/// </summary>
internal static partial class ShorthandFallbacks
{
    private static readonly string[] Collapse = ["collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces"];

    /// <summary>The shorthands this class expands, for callers that look at the cascade's declarations.</summary>
    public static readonly string[] Names = ["white-space", "border-radius", "overflow", "flex", "flex-flow", "place-items", "place-content", "place-self", "columns", "column-rule", "page-break-before", "page-break-after", "page-break-inside", "grid-area", "grid-row", "grid-column", "grid-template", "grid", "border-image"];

    public static bool IsHandled(string name)
    {
        foreach (string n in Names)
        {
            if (name.Equals(n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Expands the shorthand into longhand declarations appended to <paramref name="output"/>. Returns false when the value does not match the shorthand's grammar.</summary>
    public static bool Expand(string name, ComponentValueList value, List<(PropertyId Id, ComponentValueList Value)> output)
    {
        CssWideKeyword wide = CssWideKeywords.Detect(value);
        if (wide is CssWideKeyword.Initial or CssWideKeyword.Inherit or CssWideKeyword.Unset or CssWideKeyword.Revert or CssWideKeyword.RevertLayer)
        {
            foreach (PropertyId id in Longhands(name))
            {
                output.Add((id, value));
            }

            return true;
        }

        if (IsGridShorthand(name))
        {
            return ExpandGrid(name, value, output);
        }

        if (name.Equals("white-space", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandWhiteSpace(value, output);
        }

        if (name.Equals("border-radius", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandRadius(value, output);
        }

        if (name.Equals("border-image", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandBorderImage(value, output);
        }

        if (name.Equals("flex", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandFlex(value, output);
        }

        if (name.Equals("flex-flow", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandFlexFlow(value, output);
        }

        if (name.StartsWith("place-", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandPlace(name, value, output);
        }

        if (name.Equals("columns", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandColumns(value, output);
        }

        if (name.Equals("column-rule", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandColumnRule(value, output);
        }

        if (name.StartsWith("page-break-", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandPageBreak(name, value, output);
        }

        return ExpandOverflow(value, output);
    }

    private static ReadOnlySpan<PropertyId> Longhands(string name)
    {
        if (IsGridShorthand(name))
        {
            return GridLonghands(name);
        }

        if (name.Equals("white-space", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.WhiteSpaceCollapse, PropertyId.TextWrapMode };
        }

        if (name.Equals("border-radius", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius };
        }

        if (name.Equals("border-image", StringComparison.OrdinalIgnoreCase))
        {
            return BorderImageLonghands;
        }

        if (name.Equals("flex", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.FlexGrow, PropertyId.FlexShrink, PropertyId.FlexBasis };
        }

        if (name.Equals("flex-flow", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.FlexDirection, PropertyId.FlexWrap };
        }

        if (name.Equals("place-items", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignItems };
        }

        if (name.Equals("place-content", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignContent, PropertyId.JustifyContent };
        }

        if (name.Equals("place-self", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignSelf };
        }

        if (name.Equals("columns", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.ColumnWidth, PropertyId.ColumnCount };
        }

        if (name.Equals("column-rule", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.ColumnRuleWidth, PropertyId.ColumnRuleStyle, PropertyId.ColumnRuleColor };
        }

        if (name.Equals("page-break-before", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.BreakBefore };
        }

        if (name.Equals("page-break-after", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.BreakAfter };
        }

        if (name.Equals("page-break-inside", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.BreakInside };
        }

        return new[] { PropertyId.OverflowX, PropertyId.OverflowY };
    }

    private static ComponentValueList Text(string css) => CssTree.ParseComponentValueList(css).Values;

    private static bool ExpandWhiteSpace(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        // CSS Text 4 §4.3: white-space: normal | pre | pre-wrap | pre-line | <'white-space-collapse'> || <'text-wrap-mode'> || <'white-space-trim'>
        string? collapse = null;
        string? wrap = null;
        int count = 0;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (++count > 3 || part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Ident)
            {
                return false;
            }

            string word = part.Raw.ToString().ToLowerInvariant();
            switch (word)
            {
                case "normal": collapse = "collapse"; wrap = "wrap"; break;
                case "pre": collapse = "preserve"; wrap = "nowrap"; break;
                case "pre-wrap": collapse = "preserve"; wrap = "wrap"; break;
                case "pre-line": collapse = "preserve-breaks"; wrap = "wrap"; break;
                case "wrap" or "nowrap": wrap = word; break;
                case "trim-inner" or "discard-before" or "discard-after" or "discard-inner": break; // white-space-trim is not implemented
                default:
                    if (Array.IndexOf(Collapse, word) < 0)
                    {
                        return false;
                    }

                    collapse = word;
                    break;
            }
        }

        if (count == 0)
        {
            return false;
        }

        output.Add((PropertyId.WhiteSpaceCollapse, Text(collapse ?? "collapse")));
        output.Add((PropertyId.TextWrapMode, Text(wrap ?? "wrap")));
        return true;
    }

    private static bool ExpandRadius(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        // CSS Backgrounds 3 §5.4: <length-percentage [0,∞]>{1,4} [ / <length-percentage [0,∞]>{1,4} ]?
        var horizontal = new string[4];
        var vertical = new string[4];
        int h = 0, v = 0;
        bool slash = false;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Delim && part.Raw.Length == 1 && part.Raw[0] == '/')
            {
                if (slash || h == 0)
                {
                    return false;
                }

                slash = true;
                continue;
            }

            if (slash)
            {
                if (v == 4)
                {
                    return false;
                }

                vertical[v++] = part.Raw.ToString();
            }
            else
            {
                if (h == 4)
                {
                    return false;
                }

                horizontal[h++] = part.Raw.ToString();
            }
        }

        if (h == 0 || (slash && v == 0))
        {
            return false;
        }

        if (!slash)
        {
            Array.Copy(horizontal, vertical, 4);
            v = h;
        }

        Fill(horizontal, h);
        Fill(vertical, v);
        PropertyId[] corners = [PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius];
        for (int i = 0; i < 4; i++)
        {
            output.Add((corners[i], Text(horizontal[i] + " " + vertical[i])));
        }

        return true;
    }

    // The usual 1-to-4 value expansion: missing sides copy from the opposite side (CSS Values 4 §... "four-sided shorthand").
    private static void Fill(string[] sides, int count)
    {
        if (count == 1)
        {
            sides[1] = sides[2] = sides[3] = sides[0];
        }
        else if (count == 2)
        {
            sides[2] = sides[0];
            sides[3] = sides[1];
        }
        else if (count == 3)
        {
            sides[3] = sides[1];
        }
    }

    private static bool ExpandOverflow(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? x = null, y = null;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Ident || y is not null)
            {
                return false;
            }

            if (x is null)
            {
                x = part.Raw.ToString();
            }
            else
            {
                y = part.Raw.ToString();
            }
        }

        if (x is null)
        {
            return false;
        }

        output.Add((PropertyId.OverflowX, Text(x)));
        output.Add((PropertyId.OverflowY, Text(y ?? x)));
        return true;
    }

    private static bool ExpandFlex(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        // Flexbox §7.2: none | [ <'flex-grow'> <'flex-shrink'>? || <'flex-basis'> ]. A lone number sets flex-basis to 0.
        string? basis = null;
        string?[] numbers = new string?[3];
        int numberCount = 0, parts = 0;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (++parts > 3)
            {
                return false;
            }

            if (part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Number)
            {
                numbers[numberCount++] = part.Raw.ToString();
            }
            else if (basis is null)
            {
                basis = part.Raw.ToString();
            }
            else
            {
                return false;
            }
        }

        if (parts == 0)
        {
            return false;
        }

        if (basis is not null && numberCount == 0 && basis.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (parts != 1)
            {
                return false;
            }

            output.Add((PropertyId.FlexGrow, Text("0")));
            output.Add((PropertyId.FlexShrink, Text("0")));
            output.Add((PropertyId.FlexBasis, Text("auto")));
            return true;
        }

        if (basis is null && numberCount == 3)
        {
            // flex: 1 1 0 — a unitless zero in the last position is the basis.
            basis = numbers[2];
            numberCount = 2;
        }

        if (numberCount > 2)
        {
            return false;
        }

        output.Add((PropertyId.FlexGrow, Text(numberCount > 0 ? numbers[0]! : "1")));
        output.Add((PropertyId.FlexShrink, Text(numberCount > 1 ? numbers[1]! : "1")));
        output.Add((PropertyId.FlexBasis, Text(basis ?? (numberCount > 0 ? "0px" : "auto"))));
        if (numberCount == 0 && basis is not null && basis.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    // Multicol 1 §3.1: columns: [ <'column-width'> || <'column-count'> ]; "auto" stands for whichever the other part did not set.
    private static bool ExpandColumns(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? width = null, count = null;
        int autos = 0, parts = 0;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (++parts > 2 || part.Kind != ComponentValueKind.Token)
            {
                return false;
            }

            if (part.Token.Kind == CssTokenKind.Ident && part.Raw.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                autos++;
            }
            else if (part.Token.Kind == CssTokenKind.Number && part.Raw.ToString() != "0")
            {
                if (count is not null)
                {
                    return false;
                }

                count = part.Raw.ToString();
            }
            else if (part.Token.Kind is CssTokenKind.Dimension or CssTokenKind.Number)
            {
                if (width is not null)
                {
                    return false;
                }

                width = part.Raw.ToString();
            }
            else
            {
                return false;
            }
        }

        if (parts == 0 || (parts - autos) > 2 || (width is not null ? 1 : 0) + (count is not null ? 1 : 0) + autos != parts)
        {
            return false;
        }

        output.Add((PropertyId.ColumnWidth, Text(width ?? "auto")));
        output.Add((PropertyId.ColumnCount, Text(count ?? "auto")));
        return true;
    }

    // Multicol 1 §4.2: column-rule: <'column-rule-width'> || <'column-rule-style'> || <'column-rule-color'>.
    private static bool ExpandColumnRule(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? width = null, style = null, color = null;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            string text = part.Raw.ToString();
            bool isIdent = part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Ident;
            if (isIdent && Array.Exists(RuleStyles, w => w.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                if (style is not null)
                {
                    return false;
                }

                style = text;
            }
            else if ((isIdent && (text.Equals("thin", StringComparison.OrdinalIgnoreCase) || text.Equals("medium", StringComparison.OrdinalIgnoreCase) || text.Equals("thick", StringComparison.OrdinalIgnoreCase)))
                || (part.Kind == ComponentValueKind.Token && part.Token.Kind is CssTokenKind.Dimension or CssTokenKind.Number))
            {
                if (width is not null)
                {
                    return false;
                }

                width = text;
            }
            else
            {
                if (color is not null)
                {
                    return false;
                }

                color = text;
            }
        }

        if (width is null && style is null && color is null)
        {
            return false;
        }

        output.Add((PropertyId.ColumnRuleWidth, Text(width ?? "medium")));
        output.Add((PropertyId.ColumnRuleStyle, Text(style ?? "none")));
        output.Add((PropertyId.ColumnRuleColor, Text(color ?? "currentcolor")));
        return true;
    }

    private static readonly string[] RuleStyles = ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset"];

    // Fragmentation 3 §3.4: page-break-before/after/inside are legacy shorthands of break-before/after/inside; always means page.
    private static bool ExpandPageBreak(string name, ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? word = null;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (word is not null || part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Ident)
            {
                return false;
            }

            word = part.Raw.ToString();
        }

        if (word is null)
        {
            return false;
        }

        bool inside = name.EndsWith("inside", StringComparison.OrdinalIgnoreCase);
        if (word.Equals("always", StringComparison.OrdinalIgnoreCase))
        {
            if (inside)
            {
                return false;
            }

            word = "page";
        }
        else if (!(word.Equals("auto", StringComparison.OrdinalIgnoreCase) || word.Equals("avoid", StringComparison.OrdinalIgnoreCase)
            || (!inside && (word.Equals("left", StringComparison.OrdinalIgnoreCase) || word.Equals("right", StringComparison.OrdinalIgnoreCase)))))
        {
            return false;
        }

        PropertyId id = inside ? PropertyId.BreakInside : name.EndsWith("before", StringComparison.OrdinalIgnoreCase) ? PropertyId.BreakBefore : PropertyId.BreakAfter;
        output.Add((id, Text(word)));
        return true;
    }

    private static bool ExpandFlexFlow(ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        string? direction = null, wrap = null;
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Ident)
            {
                return false;
            }

            string word = part.Raw.ToString();
            if (word.StartsWith("row", StringComparison.OrdinalIgnoreCase) || word.StartsWith("column", StringComparison.OrdinalIgnoreCase))
            {
                if (direction is not null)
                {
                    return false;
                }

                direction = word;
            }
            else
            {
                if (wrap is not null)
                {
                    return false;
                }

                wrap = word;
            }
        }

        if (direction is null && wrap is null)
        {
            return false;
        }

        output.Add((PropertyId.FlexDirection, Text(direction ?? "row")));
        output.Add((PropertyId.FlexWrap, Text(wrap ?? "nowrap")));
        return true;
    }

    private static bool ExpandPlace(string name, ComponentValueList value, List<(PropertyId, ComponentValueList)> output)
    {
        // Box Alignment 3 §8: place-* is <align> <justify>?; the justify half is only modeled for place-content.
        var words = new List<string>(4);
        foreach (ComponentValue part in value)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (part.Kind != ComponentValueKind.Token || part.Token.Kind != CssTokenKind.Ident || words.Count == 4)
            {
                return false;
            }

            words.Add(part.Raw.ToString());
        }

        if (words.Count == 0)
        {
            return false;
        }

        int split = words[0].ToLowerInvariant() is "safe" or "unsafe" or "first" or "last" ? 2 : 1;
        if (split > words.Count)
        {
            return false;
        }

        string first = string.Join(' ', words.GetRange(0, split));
        string second = split < words.Count ? string.Join(' ', words.GetRange(split, words.Count - split)) : first;
        if (name.Equals("place-items", StringComparison.OrdinalIgnoreCase))
        {
            output.Add((PropertyId.AlignItems, Text(first)));
        }
        else if (name.Equals("place-self", StringComparison.OrdinalIgnoreCase))
        {
            output.Add((PropertyId.AlignSelf, Text(first)));
        }
        else
        {
            output.Add((PropertyId.AlignContent, Text(first)));
            output.Add((PropertyId.JustifyContent, Text(second)));
        }

        return true;
    }
}
