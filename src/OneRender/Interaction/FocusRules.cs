using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

/// <summary>Reads of element attributes and the HTML rules for which elements can take focus (HTML Standard §6.6).</summary>
internal static class FocusRules
{
    public static bool HasAttribute(Node n, string name)
    {
        foreach (Attr a in n.Attributes)
        {
            if (a.LocalName.Equals(name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetAttribute(Node n, string name, out ReadOnlySpan<char> value)
    {
        foreach (Attr a in n.Attributes)
        {
            if (a.LocalName.Equals(name, StringComparison.Ordinal))
            {
                value = a.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool IsHtml(Node n, string localName) =>
        n.IsElement && n.Namespace == ElementNamespace.Html && n.LocalName.Equals(localName, StringComparison.Ordinal);

    // HTML §2.3.4.1 "rules for parsing integers", enough for tabindex: optional whitespace, optional sign, digits; trailing text ignored.
    public static bool TryParseInteger(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        int i = 0;
        while (i < s.Length && s[i] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            i++;
        }

        bool negative = false;
        if (i < s.Length && s[i] is '-' or '+')
        {
            negative = s[i] == '-';
            i++;
        }

        if (i >= s.Length || s[i] < '0' || s[i] > '9')
        {
            return false;
        }

        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            v = (v * 10) + (s[i] - '0');
            if (v > int.MaxValue)
            {
                v = int.MaxValue;
            }

            i++;
        }

        value = (int)(negative ? -v : v);
        return true;
    }

    /// <summary>The parsed <c>tabindex</c> of an element, when it has a valid one.</summary>
    public static bool TryGetTabIndex(Node n, out int value)
    {
        value = 0;
        return TryGetAttribute(n, "tabindex", out ReadOnlySpan<char> raw) && TryParseInteger(raw, out value);
    }

    /// <summary>True for elements that take focus by default, without a <c>tabindex</c> (HTML §6.6.3).</summary>
    public static bool IsDefaultFocusable(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        string name = n.LocalName;
        switch (name)
        {
            case "a":
            case "area":
                return HasAttribute(n, "href");
            case "button":
            case "select":
            case "textarea":
                return !IsDisabled(n);
            case "input":
                if (TryGetAttribute(n, "type", out ReadOnlySpan<char> type) && type.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return !IsDisabled(n);
            case "summary":
                return IsFirstSummaryOfDetails(n);
            case "iframe":
                return true;
        }

        return IsEditingHost(n);
    }

    public static bool IsEditingHost(Node n)
    {
        if (!TryGetAttribute(n, "contenteditable", out ReadOnlySpan<char> v))
        {
            return false;
        }

        return v.IsEmpty || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("plaintext-only", StringComparison.OrdinalIgnoreCase);
    }

    // HTML §4.10.18.5: a form control is disabled by its own attribute or by a disabled fieldset ancestor, except in its first legend.
    public static bool IsDisabled(Node n)
    {
        if (HasAttribute(n, "disabled"))
        {
            return true;
        }

        Node child = n;
        for (Node p = n.Parent; !p.IsNull; child = p, p = p.Parent)
        {
            if (!IsHtml(p, "fieldset") || !HasAttribute(p, "disabled"))
            {
                continue;
            }

            Node firstLegend = default;
            for (Node c = p.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (IsHtml(c, "legend"))
                {
                    firstLegend = c;
                    break;
                }
            }

            if (firstLegend.IsNull || !firstLegend.Equals(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFirstSummaryOfDetails(Node n)
    {
        Node p = n.Parent;
        if (!IsHtml(p, "details"))
        {
            return false;
        }

        for (Node c = p.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (IsHtml(c, "summary"))
            {
                return c.Equals(n);
            }
        }

        return false;
    }

    /// <summary>True for controls where arrow keys, space and Home/End edit or move a caret instead of scrolling.</summary>
    public static bool IsTextEntry(Node n)
    {
        if (IsHtml(n, "textarea") || IsHtml(n, "select"))
        {
            return true;
        }

        if (IsHtml(n, "input"))
        {
            if (!TryGetAttribute(n, "type", out ReadOnlySpan<char> t))
            {
                return true;
            }

            foreach (string nonText in NonTextInputTypes)
            {
                if (t.Equals(nonText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        return IsEditingHost(n);
    }

    private static readonly string[] NonTextInputTypes = ["button", "submit", "reset", "checkbox", "radio", "image", "hidden", "file", "color", "range"];

    /// <summary>Whether the element is being rendered: it has a box, is not <c>display: none</c>, and is visible.</summary>
    public static bool IsRendered(Node n, StyledTree styled)
    {
        StyledNode s = styled.FindBySource(n.Index);
        if (s.IsNull)
        {
            return false;
        }

        ref readonly ComputedValues v = ref s.Values;
        return !v.Display.IsNone && !v.Display.IsContents && v.Visibility == VisibilityKind.Visible;
    }

    /// <summary>The element can be focused by a script or a click: HTML §6.6.3's focusable areas that are rendered and not inert.</summary>
    public static bool IsFocusable(Node n, StyledTree styled)
    {
        if (!n.IsElement || !IsRendered(n, styled))
        {
            return false;
        }

        for (Node a = n; a.IsElement; a = a.Parent)
        {
            if (HasAttribute(a, "inert"))
            {
                return false;
            }
        }

        if (TryGetTabIndex(n, out _))
        {
            return !((IsHtml(n, "button") || IsHtml(n, "input") || IsHtml(n, "select") || IsHtml(n, "textarea")) && IsDisabled(n));
        }

        return IsDefaultFocusable(n);
    }
}
