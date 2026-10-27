using System.Text;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>
/// <c>@property</c> registrations (CSS Properties and Values API 1 §2). The cascade does not read them, so a custom property
/// that a page declares only through <c>initial-value</c> (Tailwind's <c>--tw-border-style</c>, for one) would be missing and every
/// <c>var()</c> of it would invalidate its declaration. The registered initial values are handed back to the cascade as a
/// user-origin sheet, which any author declaration overrides.
/// </summary>
internal static class PropertyRegistrations
{
    private const int MaxRegistrations = 4096;
    private const int MaxRuleDepth = 16;
    private const int MaxValueLength = 4096;

    /// <summary>
    /// The text of a user-origin sheet that gives every registered custom property its initial value, or null when the document
    /// registers none. A property that does not inherit gets the value on every element and pseudo-element; one that inherits
    /// gets it on the root.
    /// </summary>
    /// <param name="styles">The cascade whose sheets are read.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    public static string? InitialValueSheet(DocumentStyles styles, CancellationToken cancellationToken)
    {
        StringBuilder? local = null;
        StringBuilder? inherited = null;
        int count = 0;
        var stack = new Stack<IEnumerator<CssNode>>();
        foreach (StyleSheetInfo info in styles.Sheets)
        {
            stack.Push(((IEnumerable<CssNode>)info.Sheet.Rules).GetEnumerator());
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerator<CssNode> rules = stack.Peek();
                if (!rules.MoveNext())
                {
                    rules.Dispose();
                    stack.Pop();
                    continue;
                }

                CssNode node = rules.Current;
                if (node.Kind != CssNodeKind.AtRule)
                {
                    continue;
                }

                if (node.AtRuleKind is AtRuleKind.Layer or AtRuleKind.Supports && node.HasBlock && stack.Count < MaxRuleDepth)
                {
                    stack.Push(node.Rules.GetEnumerator());
                    continue;
                }

                if (node.AtRuleKind != AtRuleKind.Unknown || !node.NameEqualsIgnoreCase("property") || count >= MaxRegistrations)
                {
                    continue;
                }

                string name = node.PreludeText.Trim().ToString();
                if (!IsCustomPropertyName(name) || !Read(node, out string initial, out bool inherits))
                {
                    continue;
                }

                count++;
                if (inherits)
                {
                    inherited ??= new StringBuilder();
                    inherited.Append(name).Append(':').Append(initial).Append(';');
                }
                else
                {
                    local ??= new StringBuilder();
                    local.Append(name).Append(':').Append(initial).Append(';');
                }
            }
        }

        if (local is null && inherited is null)
        {
            return null;
        }

        var sheet = new StringBuilder();
        if (local is not null)
        {
            sheet.Append("*,*::before,*::after{").Append(local).Append('}');
        }

        if (inherited is not null)
        {
            sheet.Append(":root{").Append(inherited).Append('}');
        }

        return sheet.ToString();
    }

    private static bool Read(CssNode rule, out string initial, out bool inherits)
    {
        initial = string.Empty;
        inherits = false;
        bool hasInherits = false;
        bool hasInitial = false;
        string syntax = string.Empty;
        foreach (CssNode d in rule.Declarations)
        {
            string value = d.ValueText.Trim().ToString();
            if (d.NameEqualsIgnoreCase("syntax"))
            {
                syntax = value;
            }
            else if (d.NameEqualsIgnoreCase("inherits"))
            {
                // §2.1: "inherits" takes the keywords true and false.
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    inherits = true;
                    hasInherits = true;
                }
                else if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    inherits = false;
                    hasInherits = true;
                }
            }
            else if (d.NameEqualsIgnoreCase("initial-value") && value.Length is > 0 and <= MaxValueLength && value.AsSpan().IndexOfAny('{', '}', ';') < 0)
            {
                initial = value;
                hasInitial = true;
            }
        }

        // §2.1: a rule without "syntax" or "inherits" is invalid, and so is an initial-value without a universal syntax that is not
        // computationally independent. Only the first two are checked here; the value is used as written.
        return hasInitial && hasInherits && syntax.Length > 0;
    }

    private static bool IsCustomPropertyName(string name)
    {
        if (name.Length < 3 || name.Length > 128 || name[0] != '-' || name[1] != '-')
        {
            return false;
        }

        for (int i = 2; i < name.Length; i++)
        {
            char c = name[i];
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
