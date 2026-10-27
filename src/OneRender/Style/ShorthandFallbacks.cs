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
    public static readonly string[] Names = ["white-space", "border-radius", "overflow", "flex", "flex-flow", "place-items", "place-content", "place-self", "grid-area", "grid-row", "grid-column", "grid-template", "grid", "border-image"];

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
