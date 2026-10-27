using System.Globalization;
using System.Text.RegularExpressions;

namespace OneRender.WptRunner;

/// <summary>An inclusive integer range used by fuzzy matching.</summary>
public readonly record struct IntRange(int Min, int Max)
{
    /// <summary>True when <paramref name="v"/> lies in the range.</summary>
    public bool Contains(long v) => v >= Min && v <= Max;
}

/// <summary>
/// A <c>&lt;meta name=fuzzy&gt;</c> allowance. <see cref="Reference"/> is the WPT-relative path it is restricted to, or null for every reference.
/// </summary>
public sealed record FuzzySpec(string? Reference, IntRange MaxDifference, IntRange TotalPixels);

/// <summary>
/// A <c>&lt;meta name=reftest-pages&gt;</c> range list. <see cref="Reference"/> is the WPT-relative path of the reference it selects pages of,
/// or null for the test document itself (WPT keys page ranges by document, like fuzzy allowances).
/// </summary>
public sealed record PageRangeSpec(string? Reference, string Ranges);

/// <summary>A <c>&lt;link rel=match|mismatch&gt;</c> from a test (or a reference, for chains).</summary>
public sealed record ReferenceLink(string Path, bool Mismatch);

/// <summary>What the runner needs to know about one test or reference file.</summary>
public sealed class ReftestFile
{
    /// <summary>WPT-relative path with forward slashes, for example <c>css/css-flexbox/foo.html</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Links to references, in document order.</summary>
    public required List<ReferenceLink> References { get; init; }

    /// <summary>Fuzzy allowances declared by this file.</summary>
    public required List<FuzzySpec> Fuzzy { get; init; }

    /// <summary>The root element carries <c>reftest-wait</c>.</summary>
    public bool ReftestWait { get; init; }

    /// <summary>The file contains script (other than the testharness).</summary>
    public bool HasScript { get; init; }

    /// <summary>The file loads testharness.js.</summary>
    public bool TestHarness { get; init; }

    /// <summary>The <c>reftest-pages</c> metas (print reftests); a document without an entry is compared on every page.</summary>
    public List<PageRangeSpec> PageRanges { get; init; } = [];

    /// <summary>
    /// The page ranges this test (as the root of a comparison) declares for the document at <paramref name="documentPath"/>: those with that
    /// reference prefix, or the unprefixed ones when it is the test itself. Null means every page.
    /// </summary>
    public string? RangesFor(string documentPath)
    {
        foreach (PageRangeSpec r in PageRanges)
        {
            if (r.Reference is null ? string.Equals(documentPath, Path, StringComparison.Ordinal) : string.Equals(r.Reference, documentPath, StringComparison.Ordinal))
            {
                return r.Ranges;
            }
        }

        return null;
    }
}

/// <summary>Reads reftest metadata out of WPT test files.</summary>
public static partial class ReftestParser
{
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<(link|meta)\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(""[^""]*""|'[^']*'|[^\s""'>]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AttrRegex();

    [GeneratedRegex(@"<html\b[^>]*\bclass\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlClassRegex();

    [GeneratedRegex(@"<script\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptRegex();

    /// <summary>Parses <paramref name="source"/>, the text of the WPT file at <paramref name="path"/>.</summary>
    public static ReftestFile Parse(string path, string source)
    {
        string text = CommentRegex().Replace(source, string.Empty);
        var refs = new List<ReferenceLink>();
        var fuzzy = new List<FuzzySpec>();
        var pages = new List<PageRangeSpec>();
        foreach (Match m in TagRegex().Matches(text))
        {
            Dictionary<string, string> attrs = Attributes(m.Groups[2].Value);
            if (m.Groups[1].Value.Equals("link", StringComparison.OrdinalIgnoreCase))
            {
                if (attrs.TryGetValue("rel", out string? rel) && attrs.TryGetValue("href", out string? href))
                {
                    foreach (string token in rel.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        bool match = token.Equals("match", StringComparison.OrdinalIgnoreCase);
                        if (match || token.Equals("mismatch", StringComparison.OrdinalIgnoreCase))
                        {
                            refs.Add(new ReferenceLink(Resolve(path, href), !match));
                        }
                    }
                }
            }
            else if (attrs.TryGetValue("name", out string? pn)
                     && pn.Equals("reftest-pages", StringComparison.OrdinalIgnoreCase)
                     && attrs.TryGetValue("content", out string? pc))
            {
                string body = pc.Trim();
                int colon = body.IndexOf(':', StringComparison.Ordinal);
                pages.Add(colon > 0 ? new PageRangeSpec(Resolve(path, body[..colon].Trim()), body[(colon + 1)..].Trim()) : new PageRangeSpec(null, body));
            }
            else if (attrs.TryGetValue("name", out string? name)
                     && name.Equals("fuzzy", StringComparison.OrdinalIgnoreCase)
                     && attrs.TryGetValue("content", out string? content)
                     && TryParseFuzzy(path, content, out FuzzySpec? spec))
            {
                fuzzy.Add(spec);
            }
        }

        bool wait = false;
        Match html = HtmlClassRegex().Match(text);
        if (html.Success)
        {
            wait = Unquote(html.Groups[1].Value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("reftest-wait", StringComparer.Ordinal);
        }

        bool harness = false, script = false;
        foreach (Match s in ScriptRegex().Matches(text))
        {
            string attrText = s.Groups[1].Value;
            if (attrText.Contains("testharness", StringComparison.Ordinal))
            {
                harness = true;
            }
            else
            {
                script = true;
            }
        }

        return new ReftestFile
        {
            Path = path,
            References = refs,
            Fuzzy = fuzzy,
            ReftestWait = wait,
            HasScript = script,
            TestHarness = harness,
            PageRanges = pages,
        };
    }

    /// <summary>
    /// Parses the content of a fuzzy meta: <c>[ref:]maxDifference=a[-b];totalPixels=c[-d]</c>, names optional.
    /// A single number n means exactly n (the range n-n), as the reftest documentation says.
    /// </summary>
    public static bool TryParseFuzzy(string testPath, string content, out FuzzySpec spec)
    {
        spec = null!;
        string body = content.Trim();
        string? reference = null;
        int colon = body.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && !body.AsSpan(0, colon).Contains('='))
        {
            reference = Resolve(testPath, body[..colon].Trim());
            body = body[(colon + 1)..];
        }

        string[] parts = body.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        IntRange? maxDiff = null, pixels = null;
        for (int i = 0; i < 2; i++)
        {
            string p = parts[i];
            bool named = false;
            int eq = p.IndexOf('=', StringComparison.Ordinal);
            string value = p;
            if (eq > 0)
            {
                string name = p[..eq].Trim();
                value = p[(eq + 1)..].Trim();
                named = true;
                if (name.Equals("maxDifference", StringComparison.OrdinalIgnoreCase))
                {
                    maxDiff = ParseRange(value);
                }
                else if (name.Equals("totalPixels", StringComparison.OrdinalIgnoreCase))
                {
                    pixels = ParseRange(value);
                }
                else
                {
                    return false;
                }
            }

            if (!named)
            {
                if (i == 0)
                {
                    maxDiff = ParseRange(value);
                }
                else
                {
                    pixels = ParseRange(value);
                }
            }
        }

        if (maxDiff is null || pixels is null)
        {
            return false;
        }

        spec = new FuzzySpec(reference, maxDiff.Value, pixels.Value);
        return true;
    }

    private static IntRange? ParseRange(string s)
    {
        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? new IntRange(n, n) : null;
        }

        return int.TryParse(s.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out int lo)
               && int.TryParse(s.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int hi)
            ? new IntRange(lo, hi)
            : null;
    }

    /// <summary>Resolves <paramref name="href"/> against the WPT-relative file <paramref name="from"/>; query and fragment are dropped.</summary>
    public static string Resolve(string from, string href)
    {
        int cut = href.IndexOfAny(['?', '#']);
        string h = cut >= 0 ? href[..cut] : href;
        var parts = new List<string>();
        if (!h.StartsWith('/'))
        {
            string dir = from.Contains('/', StringComparison.Ordinal) ? from[..from.LastIndexOf('/')] : string.Empty;
            parts.AddRange(dir.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (string seg in h.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else
            {
                parts.Add(Uri.UnescapeDataString(seg));
            }
        }

        return string.Join('/', parts);
    }

    private static Dictionary<string, string> Attributes(string tagBody)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in AttrRegex().Matches(tagBody))
        {
            d.TryAdd(a.Groups[1].Value, Unquote(a.Groups[2].Value));
        }

        return d;
    }

    private static string Unquote(string v) =>
        v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[^1] == v[0] ? v[1..^1] : v;
}
