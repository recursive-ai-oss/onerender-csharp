using System.IO;
using System.Linq;
using System.Globalization;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tests.Wpt;

internal enum WptOutcome
{
    Pass,
    Fail,
    NotApplicable,
}

/// <summary>The result of one call of a parsing or computed-value helper.</summary>
internal readonly record struct WptStyleResult(string File, string Kind, string Property, string Value, WptOutcome Outcome, string Detail)
{
    /// <summary>The identity used in the expectations file.</summary>
    public string Key => File + "\t" + Kind + "\t" + Property + "\t" + Value;
}

/// <summary>
/// Replays the parsing and computed-value assertions of web-platform-tests files for the properties the style pipeline
/// supports, without a browser or script engine.
/// <list type="bullet">
/// <item><description><c>test_valid_value</c>: the declaration is accepted and computes without an invalid-value diagnostic. The serialization of the
/// specified value is not checked; computed values are not kept in specified form.</description></item>
/// <item><description><c>test_invalid_value</c>: the declaration is rejected, or reported as invalid when the style is computed.</description></item>
/// <item><description><c>test_computed_value</c>: the value is put on <c>#target</c> of the file's own page, which is styled through the OneHtml
/// cascade and the HTML front end, and the computed value is serialized and compared.</description></item>
/// </list>
/// Everything else (calls built from variables, shorthand serializations, properties the pipeline does not model) is not applicable.
/// </summary>
internal static class WptStyleRunner
{
    private const double ViewportWidth = 800;
    private const double ViewportHeight = 600;

    public static string GroupOf(string property)
    {
        string p = property.ToLowerInvariant();
        if (p.StartsWith("margin", StringComparison.Ordinal) || p.StartsWith("padding", StringComparison.Ordinal))
        {
            return "margin-padding";
        }

        if (p.StartsWith("border", StringComparison.Ordinal))
        {
            return "border";
        }

        if (p is "width" or "height" or "aspect-ratio" or "box-sizing" || p.StartsWith("min-", StringComparison.Ordinal) || p.StartsWith("max-", StringComparison.Ordinal))
        {
            return "sizing";
        }

        if (p is "display" or "position" or "float" or "clear" or "top" or "right" or "bottom" or "left" or "z-index")
        {
            return "display-position";
        }

        if (p.StartsWith("overflow", StringComparison.Ordinal) || p == "visibility")
        {
            return "overflow-visibility";
        }

        if (p.StartsWith("font", StringComparison.Ordinal))
        {
            return "font";
        }

        if (p is "color" or "opacity" or "background-color")
        {
            return "color-opacity";
        }

        if (p is "content" || p.StartsWith("list-style", StringComparison.Ordinal))
        {
            return "content-lists";
        }

        return "text-inline";
    }

    /// <summary>Whether the pipeline models the property, or expands it as a shorthand into properties it models.</summary>
    private static bool Supported(string property)
    {
        if (Properties.TryGet(property, out _) || ShorthandFallbacks.IsHandled(property))
        {
            return true;
        }

        ShorthandExpansion e = Shorthands.Expand(property.ToLowerInvariant(), CssTree.ParseComponentValueList("inherit").Values);
        return e.Status == ShorthandStatus.Expanded;
    }

    public static List<WptStyleResult> RunFile(string root, string path)
    {
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        string html = File.ReadAllText(path);
        string scripts = InlineScripts(html);
        List<WptCall> calls = WptCallExtractor.Extract(scripts, out int unreadable);
        var results = new List<WptStyleResult>(calls.Count + 1);
        if (unreadable > 0)
        {
            results.Add(new WptStyleResult(rel, "unreadable", "(calls)", unreadable.ToString(CultureInfo.InvariantCulture), WptOutcome.NotApplicable, "arguments are not string literals"));
        }

        foreach (WptCall c in calls)
        {
            string kind = c.Function switch
            {
                "test_invalid_value" => "invalid",
                "test_computed_value" => "computed",
                _ => "valid",
            };
            results.Add(RunCall(rel, html, kind, c));
        }

        return results;
    }

    private static WptStyleResult RunCall(string file, string html, string kind, WptCall c)
    {
        WptStyleResult Result(WptOutcome o, string detail) => new(file, kind, c.Property, c.Value, o, detail);
        if (!Supported(c.Property))
        {
            return Result(WptOutcome.NotApplicable, "property not modelled");
        }

        if (c.Value.Contains("var(", StringComparison.Ordinal) || c.Value.Contains("!important", StringComparison.Ordinal))
        {
            return Result(WptOutcome.NotApplicable, "var() or !important in a declaration value");
        }

        if (kind == "computed")
        {
            return RunComputed(html, c, Result);
        }

        var tree = new StyledTree();
        tree.Styles.SetViewport(ViewportWidth, ViewportHeight);
        var d = new StyleDeclaration();
        bool accepted = d.TrySet(c.Property, c.Value);
        bool invalid = !accepted;
        if (accepted)
        {
            StyledNode root = tree.AddRoot("html", d);
            _ = root.Values;
            invalid = tree.Diagnostics.Items.Any(x => x.Kind == DiagnosticKind.InvalidValue);
        }

        if (kind == "invalid")
        {
            return invalid ? Result(WptOutcome.Pass, string.Empty) : Result(WptOutcome.Fail, "accepted");
        }

        return invalid ? Result(WptOutcome.Fail, "rejected") : Result(WptOutcome.Pass, string.Empty);
    }

    private static WptStyleResult RunComputed(string html, WptCall c, Func<WptOutcome, string, WptStyleResult> result)
    {
        if (!Properties.TryGet(c.Property, out PropertyId id))
        {
            return result(WptOutcome.NotApplicable, "shorthand: its serialization is not modelled");
        }

        string expected = c.Expected ?? c.Value;
        int end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        string page = html.Insert(end < 0 ? html.Length : end, "<style>#target{" + c.Property + ":" + c.Value + "}</style>");
        Document doc = Document.Parse(page);
        Node target = doc.GetElementById("target");
        if (target.IsNull)
        {
            return result(WptOutcome.NotApplicable, "no #target in the page");
        }

        var context = new StyleContext { Media = new MediaFeatures { Width = ViewportWidth, Height = ViewportHeight } };
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
        var table = new StyleTable();
        table.SetViewport(ViewportWidth, ViewportHeight);
        var tree = new StyledTree(table);
        new HtmlFrontEnd(tree).Build(styles);
        StyledNode node = tree.FindBySource(target.Index);
        if (node.IsNull)
        {
            return result(WptOutcome.NotApplicable, "#target has no box in the styled tree");
        }

        string actual = ComputedValueText.Get(id, in node.Values);
        return actual == expected ? result(WptOutcome.Pass, string.Empty) : result(WptOutcome.Fail, "expected '" + expected + "', got '" + actual + "'");
    }

    /// <summary>The text of the inline script elements of the page.</summary>
    private static string InlineScripts(string html)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (true)
        {
            int open = html.IndexOf("<script", i, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                break;
            }

            int gt = html.IndexOf('>', open);
            int close = gt < 0 ? -1 : html.IndexOf("</script>", gt, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                break;
            }

            if (!html.AsSpan(open, gt - open).Contains("src=", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(html, gt + 1, close - gt - 1).Append('\n');
            }

            i = close + 9;
        }

        return sb.ToString();
    }
}
