using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>
/// Shorthands the OneHtml cascade keeps as one token list (it has no grammar for them) but whose longhands the engine
/// computes: <c>white-space</c>, <c>border-radius</c> and <c>overflow</c>. Both front ends expand them here.
/// </summary>
internal static partial class ShorthandFallbacks
{
    private static readonly string[] Collapse = ["collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces"];

    public static bool IsHandled(string name) =>
        name.Equals("white-space", StringComparison.OrdinalIgnoreCase) || name.Equals("border-radius", StringComparison.OrdinalIgnoreCase) || name.Equals("overflow", StringComparison.OrdinalIgnoreCase)
        || IsGridShorthand(name);

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
}
