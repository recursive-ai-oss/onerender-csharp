using OneRender.Interaction;
using RecursiveAI.OneHtml;

namespace OneRender.Tree;

/// <summary>Finds elements of a OneHtml document by the names a test harness uses.</summary>
internal static class DocumentNames
{
    /// <summary>
    /// The first element in document order whose <c>id</c> is <paramref name="name"/>; failing that, the first form control (<c>button</c>,
    /// <c>fieldset</c>, <c>input</c>, <c>object</c>, <c>output</c>, <c>select</c>, <c>textarea</c>) whose <c>name</c> attribute is
    /// <paramref name="name"/>. Comparison is exact. The id lookup uses the document's id table; the name scan walks the document and
    /// allocates nothing.
    /// </summary>
    public static Node Find(Document document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            return default;
        }

        Node byId = document.GetElementById(name);
        if (!byId.IsNull)
        {
            return byId;
        }

        for (Node n = document.Root.NextInDocumentOrder(default); !n.IsNull; n = n.NextInDocumentOrder(default))
        {
            if (n.IsElement && IsNamedControl(n) && FocusRules.TryGetAttribute(n, "name", out ReadOnlySpan<char> value) && value.Equals(name, StringComparison.Ordinal))
            {
                return n;
            }
        }

        return default;
    }

    private static bool IsNamedControl(Node n) =>
        FocusRules.IsHtml(n, "input") || FocusRules.IsHtml(n, "button") || FocusRules.IsHtml(n, "select") || FocusRules.IsHtml(n, "textarea")
        || FocusRules.IsHtml(n, "fieldset") || FocusRules.IsHtml(n, "output") || FocusRules.IsHtml(n, "object");
}
