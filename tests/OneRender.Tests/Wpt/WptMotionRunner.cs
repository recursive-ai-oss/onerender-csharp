using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Tests.Wpt;

/// <summary>
/// Replays the interpolation helpers of web-platform-tests (<c>test_interpolation</c>, <c>test_no_interpolation</c>,
/// <c>test_not_animatable</c>) against the transition and animation engine, driven by the host clock and no script.
/// </summary>
/// <remarks>
/// The helper's own construction is reproduced: a target and a reference element side by side, the reference carrying the expected value
/// as an ordinary declaration; the target gets a 100 s transition or animation with a -50 s delay and a timing function that yields
/// the wanted progress at the midpoint (<c>createEasing</c>). The target's computed value must equal the reference's. A transition starts
/// from a style change, which is made by focusing the target (<c>:focus</c> holds the "to" value). The Web Animations variant of the helper
/// needs the script API and is not replayed. The comparison uses the engine's own computed-value text on both sides, so it checks
/// the interpolated value, not the serialization.
/// </remarks>
internal static class WptMotionRunner
{
    private const double ViewportWidth = 800;
    private const double ViewportHeight = 600;

    private static readonly string[] Helpers = ["test_interpolation", "test_no_interpolation", "test_not_animatable", "test_composition"];

    private static readonly double[] FlipAts = [-0.3, 0, 0.3, 0.5, 0.6, 1, 1.5];

    private enum Method
    {
        Transitions,
        TransitionsAll,
        TransitionsAllowDiscrete,
        TransitionsAllAllowDiscrete,
        Animations,
    }

    private static string NameOf(Method m) => m switch
    {
        Method.Transitions => "CSS Transitions",
        Method.TransitionsAll => "CSS Transitions with transition: all",
        Method.TransitionsAllowDiscrete => "CSS Transitions with transition-behavior:allow-discrete",
        Method.TransitionsAllAllowDiscrete => "CSS Transitions with transition-property:all and transition-behavor:allow-discrete",
        _ => "CSS Animations",
    };

    public static List<WptStyleResult> RunFile(string root, string path)
    {
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        string html = File.ReadAllText(path);
        string scripts = InlineScripts(html);
        var results = new List<WptStyleResult>();
        foreach ((string helper, JsValue? options, JsValue? expectations) in ExtractCalls(scripts))
        {
            if (helper == "test_composition")
            {
                results.Add(new WptStyleResult(rel, "composition", options?.GetString("property") ?? "?", "(composition)", WptOutcome.NotApplicable, "animation-composition is not implemented"));
                continue;
            }

            if (options is null || options.Kind != JsKind.Object || !options.IsFullyKnown() || (expectations is not null && !expectations.IsFullyKnown()))
            {
                results.Add(new WptStyleResult(rel, "unreadable", "(calls)", "1", WptOutcome.NotApplicable, "arguments are not literals"));
                continue;
            }

            if (options.Get("comparisonFunction") is not null)
            {
                results.Add(new WptStyleResult(rel, "interpolation", options.GetString("property") ?? "?", "(custom comparison)", WptOutcome.NotApplicable, "custom comparison function"));
                continue;
            }

            RunCall(rel, html, helper, options, expectations, results);
        }

        return results;
    }

    private static void RunCall(string file, string html, string helper, JsValue options, JsValue? expectations, List<WptStyleResult> results)
    {
        string? property = options.GetString("property");
        if (property is null)
        {
            results.Add(new WptStyleResult(file, "interpolation", "?", "(no property)", WptOutcome.NotApplicable, "no literal property"));
            return;
        }

        JsValue from = options.Get("from") ?? new JsValue { Kind = JsKind.Neutral };
        JsValue to = options.Get("to") ?? new JsValue { Kind = JsKind.Neutral };
        bool plain = helper == "test_interpolation";
        bool notAnimatable = helper == "test_not_animatable";
        string? only = options.GetString("method");
        var names = new List<string>();
        if (options.Get("target_names") is { Kind: JsKind.Array } tn)
        {
            foreach (JsValue t in tn.Items)
            {
                names.Add(t.Text);
            }
        }

        Method[] methods = plain
            ? [Method.Transitions, Method.TransitionsAll, Method.Animations]
            : [Method.TransitionsAllowDiscrete, Method.TransitionsAllAllowDiscrete, Method.Transitions, Method.TransitionsAll, Method.Animations];
        foreach (Method m in methods)
        {
            string name = NameOf(m);
            if ((only is not null && only != name) || (names.Count > 0 && !names.Contains(name)) || (m is Method.TransitionsAll or Method.TransitionsAllAllowDiscrete && property.StartsWith("--", StringComparison.Ordinal)))
            {
                continue;
            }

            List<(double At, JsValue Expect)> cases = CasesFor(m, helper, options, expectations, from, to, name);
            foreach ((double at, JsValue expect) in cases)
            {
                string value = Describe(from) + " -> " + Describe(to) + " @" + at.ToString(CultureInfo.InvariantCulture);
                results.Add(RunSubtest(file, html, name, property, value, m, options, from, to, at, expect, notAnimatable));
            }
        }
    }

    private static string Describe(JsValue v) => v.Kind == JsKind.Neutral ? "neutral" : v.Text;

    private static List<(double, JsValue)> CasesFor(Method m, string helper, JsValue options, JsValue? expectations, JsValue from, JsValue to, string methodName)
    {
        var list = new List<(double, JsValue)>();
        if (helper == "test_interpolation")
        {
            JsValue? source = options.Get(methodName) is { Kind: JsKind.Array } over ? over : expectations;
            if (source is { Kind: JsKind.Array })
            {
                foreach (JsValue e in source.Items)
                {
                    if (e.Get("at") is { Kind: JsKind.Number } at && (e.Get("expect") ?? e.Get("option")) is { } ex)
                    {
                        list.Add((at.Number, ex));
                    }
                }
            }

            return list;
        }

        JsValue underlying = options.Get("underlying") ?? new JsValue { Kind = JsKind.Neutral };
        JsValue a, b;
        double flip;
        if (helper == "test_no_interpolation")
        {
            a = from;
            b = to;
            flip = m switch { Method.Animations or Method.TransitionsAllowDiscrete or Method.TransitionsAllAllowDiscrete => 0.5, _ => double.NegativeInfinity };
        }
        else
        {
            flip = double.NegativeInfinity;
            if (m == Method.Animations)
            {
                a = b = underlying;
            }
            else
            {
                a = from;
                b = to;
            }
        }

        foreach (double at in FlipAts)
        {
            list.Add((at, at < flip ? a : b));
        }

        return list;
    }

    private static WptStyleResult RunSubtest(string file, string html, string kind, string property, string value, Method method, JsValue options, JsValue from, JsValue to, double at, JsValue expect, bool notAnimatable)
    {
        WptStyleResult Result(WptOutcome o, string detail) => new(file, kind, property, value, o, detail);
        if (!Properties.TryGet(property, out PropertyId id))
        {
            return Result(WptOutcome.NotApplicable, "property not modelled");
        }

        if (expect.Kind is not (JsKind.String or JsKind.Neutral))
        {
            return Result(WptOutcome.NotApplicable, "expectation is not a single value");
        }

        // The helper clones #target-template into each target container; only a template without a .target element of its own is supported.
        string inner = string.Empty;
        Match tpl = Regex.Match(html, @"<template id=[""']?target-template[""']?>(.*?)</template>", RegexOptions.Singleline);
        if (tpl.Success)
        {
            inner = tpl.Groups[1].Value;
            if (inner.Contains("target", StringComparison.Ordinal))
            {
                return Result(WptOutcome.NotApplicable, "template with its own .target element");
            }
        }

        string? fromText = from.Kind == JsKind.String ? from.Text : null;
        string? toText = to.Kind == JsKind.String ? to.Text : null;
        string? underlying = options.GetString("underlying");
        string behavior = options.GetString("behavior") is { } bh ? "transition-behavior:" + bh + ";" : string.Empty;
        bool transitions = method != Method.Animations;
        string easing = Easing(at);
        var css = new StringBuilder("<style>");
        if (transitions)
        {
            css.Append("#actual{");
            if (notAnimatable && underlying is not null)
            {
                css.Append(property).Append(':').Append(underlying).Append(" !important;");
            }

            if (fromText is not null)
            {
                css.Append(property).Append(':').Append(fromText).Append(" !important;");
            }

            css.Append("}#actual:focus{transition-duration:100s;transition-delay:-50s;transition-timing-function:").Append(easing)
                .Append(";transition-property:").Append(method is Method.TransitionsAll or Method.TransitionsAllAllowDiscrete ? "all" : property).Append(';')
                .Append(method is Method.TransitionsAllowDiscrete or Method.TransitionsAllAllowDiscrete ? "transition-behavior:allow-discrete;" : behavior);
            if (toText is not null)
            {
                css.Append(property).Append(':').Append(toText).Append(" !important;");
            }

            css.Append('}');
        }
        else
        {
            css.Append("@keyframes kf{");
            if (fromText is not null)
            {
                css.Append("from{").Append(property).Append(':').Append(fromText).Append('}');
            }

            if (toText is not null)
            {
                css.Append("to{").Append(property).Append(':').Append(toText).Append('}');
            }

            css.Append("}#actual{");
            if (notAnimatable && underlying is not null)
            {
                css.Append(property).Append(':').Append(underlying).Append(';');
            }

            css.Append("animation-name:kf;animation-duration:100s;animation-delay:-50s;animation-timing-function:").Append(easing).Append('}');
        }

        if (expect.Kind == JsKind.String)
        {
            css.Append("#expected{").Append(property).Append(':').Append(expect.Text).Append(" !important}");
        }

        css.Append("</style>");
        string markup = "<div><div><div class=parent><div></div><div id=actual class='container target actual' tabindex=0>" + inner + "</div><div id=expected class='container target expected'>" + inner + "</div></div></div></div>";
        int end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        string page = end < 0 ? html + markup + css : html.Insert(end, markup + css);
        try
        {
            InteractivePage p = InteractivePage.Create(page, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = ViewportWidth, ViewportHeight = ViewportHeight }, new InteractionOptions { AnimationStartTime = 0 });
            Node actual = p.Page.Document.GetElementById("actual");
            if (transitions)
            {
                p.Advance(0);
                p.Focus(actual);
                if (!ReferenceEquals(p.FocusedElement.Document, actual.Document) || p.FocusedElement.Index != actual.Index)
                {
                    return Result(WptOutcome.NotApplicable, "the target could not take focus to start the transition");
                }
            }

            p.Advance(0);
            StyledNode a = p.Page.Styled.FindBySource(actual.Index);
            StyledNode e = p.Page.Styled.FindBySource(p.Page.Document.GetElementById("expected").Index);
            if (a.IsNull || e.IsNull)
            {
                return Result(WptOutcome.NotApplicable, "the target has no styled node");
            }

            string got = Normalize(ComputedValueText.Get(id, in a.Values));
            string want = Normalize(ComputedValueText.Get(id, in e.Values));
            return got == want ? Result(WptOutcome.Pass, string.Empty) : Result(WptOutcome.Fail, "expected '" + want + "', got '" + got + "'");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result(WptOutcome.Fail, "threw " + ex.GetType().Name);
        }
    }

    // The helper's createEasing: a timing function with output y at input 0.5.
    private static string Easing(double y)
    {
        if (y == 0)
        {
            return "steps(1, end)";
        }

        if (y == 1)
        {
            return "steps(1, start)";
        }

        if (y == 0.5)
        {
            return "linear";
        }

        double b = ((8 * y) - 1) / 6;
        string s = b.ToString("R", CultureInfo.InvariantCulture);
        return "cubic-bezier(0, " + s + ", 1, " + s + ")";
    }

    // roundNumbers then token spacing, as the helper's normalizeValue does.
    private static string Normalize(string value)
    {
        string r = Regex.Replace(value, @"-?\d*\.\d+(e-?\d+)?", m =>
        {
            string f = double.Parse(m.Value, CultureInfo.InvariantCulture).ToString("F2", CultureInfo.InvariantCulture);
            f = Regex.Replace(f, @"\.\d+", d => Regex.Replace(d.Value, "0+$", string.Empty));
            f = Regex.Replace(f, @"\.$", string.Empty);
            return f == "-0" ? "0" : f;
        });
        r = Regex.Replace(r, @"([\w\d.]+|[^\s])", "$1 ");
        return Regex.Replace(r, @"\s+", " ");
    }

    private static List<(string Helper, JsValue? Options, JsValue? Expectations)> ExtractCalls(string text)
    {
        var calls = new List<(string, JsValue?, JsValue?)>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int nl = text.IndexOf('\n', i);
                i = nl < 0 ? text.Length : nl + 1;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = e < 0 ? text.Length : e + 2;
            }
            else if (c is '"' or '\'' or '`')
            {
                i++;
                while (i < text.Length && text[i] != c)
                {
                    i += text[i] == '\\' ? 2 : 1;
                }

                i++;
            }
            else if (c == 't' && (i == 0 || !(char.IsAsciiLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.')))
            {
                string? name = null;
                foreach (string h in Helpers)
                {
                    if (string.CompareOrdinal(text, i, h, 0, h.Length) == 0 && i + h.Length < text.Length && text[i + h.Length] == '(')
                    {
                        name = h;
                        break;
                    }
                }

                if (name is null)
                {
                    i++;
                    continue;
                }

                var reader = new JsLiteralReader(text, i + name.Length + 1);
                JsValue first = reader.ReadValue();
                reader.SkipSpace();
                JsValue? second = null;
                if (reader.Position < text.Length && text[reader.Position] == ',')
                {
                    var r2 = new JsLiteralReader(text, reader.Position + 1);
                    second = r2.ReadValue();
                    reader = r2;
                }

                calls.Add((name, first, second));
                i = Math.Max(reader.Position, i + name.Length);
            }
            else
            {
                i++;
            }
        }

        return calls;
    }

    private static string InlineScripts(string html)
    {
        var sb = new StringBuilder();
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
