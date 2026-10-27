using OneRender.Style;
using System.Globalization;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Text;

/// <summary>The <c>font-display</c> descriptor (CSS Fonts 4 §4.7). Recorded only: a document render has no clock-driven font swap.</summary>
public enum FontDisplay : byte
{
    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>block</c>.</summary>
    Block,

    /// <summary><c>swap</c>.</summary>
    Swap,

    /// <summary><c>fallback</c>.</summary>
    Fallback,

    /// <summary><c>optional</c>.</summary>
    Optional,
}

/// <summary>Whether a <c>src</c> entry names a downloadable resource or an installed font.</summary>
public enum FontSourceKind : byte
{
    /// <summary><c>url(...)</c>.</summary>
    Url,

    /// <summary><c>local(...)</c>.</summary>
    Local,
}

/// <summary>One entry of a <c>src</c> descriptor.</summary>
public sealed class FontSource
{
    internal FontSource(FontSourceKind kind, string value, string? format, string[] tech)
    {
        Kind = kind;
        Value = value;
        Format = format;
        Tech = tech;
    }

    /// <summary>Url or local.</summary>
    public FontSourceKind Kind { get; }

    /// <summary>The URL as written (not resolved against a base), or the local font's full name or PostScript name.</summary>
    public string Value { get; }

    /// <summary>The <c>format()</c> hint in lower case (for example <c>woff2</c>), or null when absent.</summary>
    public string? Format { get; }

    /// <summary>The <c>tech()</c> keywords in lower case; empty when absent.</summary>
    public IReadOnlyList<string> Tech { get; }
}

/// <summary>An inclusive range of code points from <c>unicode-range</c>.</summary>
/// <param name="Start">The first code point.</param>
/// <param name="End">The last code point.</param>
public readonly record struct CodePointRange(int Start, int End);

/// <summary>
/// The descriptors of one <c>@font-face</c> rule, parsed and validated (CSS Fonts 4 §4). Invalid descriptor values are ignored
/// one by one, as the CSS error rules say, and recorded as diagnostics; a rule with no usable <c>font-family</c> or <c>src</c> is dropped.
/// </summary>
public sealed class FontFaceRule
{
    /// <summary>The most <c>src</c> entries kept per rule.</summary>
    public const int MaxSources = 64;

    /// <summary>The most <c>unicode-range</c> ranges kept per rule.</summary>
    public const int MaxRanges = 4096;

    private FontFaceRule(string family, FontSource[] sources)
    {
        Family = family;
        Sources = sources;
    }

    /// <summary>The family name the rule defines.</summary>
    public string Family { get; private init; }

    /// <summary>The <c>src</c> entries in order of preference.</summary>
    public IReadOnlyList<FontSource> Sources { get; private init; }

    /// <summary>The lowest weight the face covers. Default 400 (<c>font-weight: auto</c> is treated as normal).</summary>
    public float WeightMin { get; private set; } = 400;

    /// <summary>The highest weight the face covers.</summary>
    public float WeightMax { get; private set; } = 400;

    /// <summary>The narrowest stretch as a percentage. Default 100.</summary>
    public float StretchMin { get; private set; } = 100;

    /// <summary>The widest stretch as a percentage.</summary>
    public float StretchMax { get; private set; } = 100;

    /// <summary>The style kind. Default normal.</summary>
    public FontStyleKind Style { get; private set; }

    /// <summary>For oblique faces, the smallest angle in degrees. Default 14.</summary>
    public float ObliqueMin { get; private set; } = 14;

    /// <summary>For oblique faces, the largest angle in degrees.</summary>
    public float ObliqueMax { get; private set; } = 14;

    /// <summary>The code points the face may supply; empty means all (<c>U+0-10FFFF</c>).</summary>
    public IReadOnlyList<CodePointRange> UnicodeRanges { get; private set; } = [];

    /// <summary>The <c>font-display</c> value; recorded only.</summary>
    public FontDisplay Display { get; private set; }

    /// <summary>The <c>size-adjust</c> factor (1 = 100%) applied to the face's glyphs and metrics. Default 1.</summary>
    public float SizeAdjust { get; private set; } = 1;

    /// <summary><c>ascent-override</c> as a fraction of the em, or NaN for the font's own value.</summary>
    public float AscentOverride { get; private set; } = float.NaN;

    /// <summary><c>descent-override</c> as a fraction of the em, or NaN.</summary>
    public float DescentOverride { get; private set; } = float.NaN;

    /// <summary><c>line-gap-override</c> as a fraction of the em, or NaN.</summary>
    public float LineGapOverride { get; private set; } = float.NaN;

    /// <summary>The location of the stylesheet that holds the rule; passed to the resolver beside each <c>url()</c>. May be null.</summary>
    public string? BaseUrl { get; private set; }

    /// <summary>
    /// Parses a rule from descriptor name and value text pairs (the text between the colon and the semicolon).
    /// </summary>
    /// <param name="descriptors">The declarations of the rule in source order; a later declaration of a name replaces an earlier valid one.</param>
    /// <param name="baseUrl">The stylesheet location handed to the resolver with each URL; may be null.</param>
    /// <param name="diagnostics">Receives what was ignored; may be null.</param>
    /// <returns>The rule, or null when it has no valid family or no valid source.</returns>
    public static FontFaceRule? Create(IEnumerable<KeyValuePair<string, string>> descriptors, string? baseUrl = null, TextDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        string? family = null;
        FontSource[]? sources = null;
        var rule = new FontFaceRule("", []);
        var tokens = new List<Tok>();
        int count = 0;
        foreach (KeyValuePair<string, string> kv in descriptors)
        {
            if (++count > 256)
            {
                break; // a rule with hundreds of declarations is not a font rule
            }

            bool ok = CssValueTokenizer.TryTokenize(kv.Value.AsSpan(), tokens);
            string name = kv.Key.ToLowerInvariant();
            switch (name)
            {
                case "font-family":
                    string? f = ok ? ParseFamilyName(tokens) : null;
                    ok = f is not null;
                    family = f ?? family;
                    break;
                case "src":
                    FontSource[]? s = ok ? ParseSources(tokens, diagnostics) : null;
                    ok = s is not null && s.Length > 0;
                    sources = ok ? s : sources;
                    break;
                case "font-weight":
                    ok = ok && rule.ParseWeight(tokens);
                    break;
                case "font-style":
                    ok = ok && rule.ParseStyle(tokens);
                    break;
                case "font-stretch":
                case "font-width":
                    ok = ok && rule.ParseStretch(tokens);
                    break;
                case "unicode-range":
                    ok = ok && rule.ParseRanges(tokens);
                    break;
                case "font-display":
                    ok = ok && rule.ParseDisplay(tokens);
                    break;
                case "size-adjust":
                    ok = ok && tokens.Count == 1 && tokens[0].Kind == TokKind.Percent && tokens[0].Number >= 0;
                    if (ok)
                    {
                        rule.SizeAdjust = (float)(tokens[0].Number / 100);
                    }

                    break;
                case "ascent-override":
                    float asc = float.NaN;
                    ok = ok && ParseOverride(tokens, out asc);
                    rule.AscentOverride = ok ? asc : rule.AscentOverride;
                    break;
                case "descent-override":
                    float desc = float.NaN;
                    ok = ok && ParseOverride(tokens, out desc);
                    rule.DescentOverride = ok ? desc : rule.DescentOverride;
                    break;
                case "line-gap-override":
                    float gap = float.NaN;
                    ok = ok && ParseOverride(tokens, out gap);
                    rule.LineGapOverride = ok ? gap : rule.LineGapOverride;
                    break;
                default:
                    continue; // descriptors the engine does not use (font-variation-settings, font-feature-settings, ...) are not errors
            }

            if (!ok)
            {
                diagnostics?.Report(new TextDiagnostic(TextDiagnosticKind.InvalidDescriptor, Subject: name, Detail: "The value is invalid and was ignored."));
            }
        }

        if (family is null || sources is null)
        {
            diagnostics?.Report(new TextDiagnostic(TextDiagnosticKind.InvalidFontFaceRule, Subject: family, Detail: family is null ? "The rule has no valid font-family." : "The rule has no valid src."));
            return null;
        }

        rule.BaseUrl = baseUrl;
        return new FontFaceRule(family, sources)
        {
            WeightMin = rule.WeightMin,
            WeightMax = rule.WeightMax,
            StretchMin = rule.StretchMin,
            StretchMax = rule.StretchMax,
            Style = rule.Style,
            ObliqueMin = rule.ObliqueMin,
            ObliqueMax = rule.ObliqueMax,
            UnicodeRanges = rule.UnicodeRanges,
            Display = rule.Display,
            SizeAdjust = rule.SizeAdjust,
            AscentOverride = rule.AscentOverride,
            DescentOverride = rule.DescentOverride,
            LineGapOverride = rule.LineGapOverride,
            BaseUrl = baseUrl,
        };
    }

    /// <summary>Reads a rule from a parsed <c>@font-face</c> at-rule of OneHtml's CSS tree.</summary>
    /// <param name="node">An at-rule node whose name is <c>font-face</c>.</param>
    /// <param name="baseUrl">The stylesheet location (<c>Stylesheet.Location</c>); may be null.</param>
    /// <param name="diagnostics">Receives what was ignored; may be null.</param>
    /// <returns>The rule, or null when it is not valid.</returns>
    public static FontFaceRule? FromNode(CssNode node, string? baseUrl = null, TextDiagnostics? diagnostics = null)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (CssNode d in node.Declarations)
        {
            list.Add(new KeyValuePair<string, string>(d.Name, d.ValueText.ToString()));
        }

        return Create(list, baseUrl, diagnostics);
    }

    private static bool ParseOverride(List<Tok> t, out float value)
    {
        value = float.NaN;
        if (t.Count == 1 && t[0].Kind == TokKind.Ident && t[0].Text.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (t.Count == 1 && t[0].Kind == TokKind.Percent && t[0].Number >= 0)
        {
            value = (float)(t[0].Number / 100);
            return true;
        }

        return false;
    }

    private static string? ParseFamilyName(List<Tok> t)
    {
        if (t.Count == 1 && t[0].Kind == TokKind.String)
        {
            return t[0].Text;
        }

        if (t.Count == 0)
        {
            return null;
        }

        // An unquoted family is a run of identifiers joined by single spaces; a generic family or a CSS-wide keyword is not allowed here.
        var parts = new List<string>();
        foreach (Tok k in t)
        {
            if (k.Kind != TokKind.Ident)
            {
                return null;
            }

            parts.Add(k.Text);
        }

        if (parts.Count == 1 && (FontFamilyName.GenericFromKeyword(parts[0]) != GenericFamily.None || IsWideKeyword(parts[0])))
        {
            return null;
        }

        return string.Join(' ', parts);
    }

    private static bool IsWideKeyword(string s) =>
        s.Equals("inherit", StringComparison.OrdinalIgnoreCase) || s.Equals("initial", StringComparison.OrdinalIgnoreCase)
        || s.Equals("unset", StringComparison.OrdinalIgnoreCase) || s.Equals("revert", StringComparison.OrdinalIgnoreCase)
        || s.Equals("revert-layer", StringComparison.OrdinalIgnoreCase) || s.Equals("default", StringComparison.OrdinalIgnoreCase);

    private static FontSource[]? ParseSources(List<Tok> t, TextDiagnostics? diagnostics)
    {
        var result = new List<FontSource>();
        int i = 0;
        while (i < t.Count)
        {
            // One comma-separated entry: url(...) [format(...)] [tech(...)] | local(...). A malformed entry is skipped alone.
            int end = i;
            int depth = 0;
            while (end < t.Count && !(t[end].Kind == TokKind.Comma && depth == 0))
            {
                if (t[end].Kind == TokKind.Function)
                {
                    depth++;
                }
                else if (t[end].Kind == TokKind.CloseParen)
                {
                    depth--;
                }

                end++;
            }

            FontSource? src = ParseSourceEntry(t, i, end);
            if (src is not null)
            {
                if (result.Count < MaxSources)
                {
                    result.Add(src);
                }
            }
            else
            {
                diagnostics?.Report(new TextDiagnostic(TextDiagnosticKind.InvalidDescriptor, Subject: "src", Detail: "An entry is malformed and was skipped."));
            }

            i = end + 1;
        }

        return result.Count == 0 ? null : result.ToArray();
    }

    private static FontSource? ParseSourceEntry(List<Tok> t, int start, int end)
    {
        int i = start;
        if (i >= end)
        {
            return null;
        }

        string value;
        FontSourceKind kind;
        if (t[i].Kind == TokKind.Url)
        {
            kind = FontSourceKind.Url;
            value = t[i].Text;
            i++;
        }
        else if (t[i].Kind == TokKind.Function && t[i].Text.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            // url("...") with optional modifiers (url-modifiers are ignored).
            if (i + 2 >= end || t[i + 1].Kind != TokKind.String || t[i + 2].Kind != TokKind.CloseParen)
            {
                return null;
            }

            kind = FontSourceKind.Url;
            value = t[i + 1].Text;
            i += 3;
        }
        else if (t[i].Kind == TokKind.Function && t[i].Text.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            kind = FontSourceKind.Local;
            int close = i + 1;
            while (close < end && t[close].Kind != TokKind.CloseParen)
            {
                close++;
            }

            if (close >= end)
            {
                return null;
            }

            var inner = t.GetRange(i + 1, close - i - 1);
            string? name = ParseLocalName(inner);
            if (name is null)
            {
                return null;
            }

            value = name;
            i = close + 1;
        }
        else
        {
            return null;
        }

        string? format = null;
        var tech = new List<string>();
        while (i < end)
        {
            if (kind == FontSourceKind.Url && t[i].Kind == TokKind.Function && t[i].Text.Equals("format", StringComparison.OrdinalIgnoreCase) && format is null)
            {
                if (i + 2 >= end || (t[i + 1].Kind is not (TokKind.String or TokKind.Ident)) || t[i + 2].Kind != TokKind.CloseParen)
                {
                    return null;
                }

                format = t[i + 1].Text.ToLowerInvariant();
                i += 3;
            }
            else if (kind == FontSourceKind.Url && t[i].Kind == TokKind.Function && t[i].Text.Equals("tech", StringComparison.OrdinalIgnoreCase) && tech.Count == 0)
            {
                i++;
                while (i < end && t[i].Kind != TokKind.CloseParen)
                {
                    if (t[i].Kind == TokKind.Ident)
                    {
                        tech.Add(t[i].Text.ToLowerInvariant());
                    }
                    else if (t[i].Kind != TokKind.Comma)
                    {
                        return null;
                    }

                    i++;
                }

                if (i >= end)
                {
                    return null;
                }

                i++;
            }
            else
            {
                return null;
            }
        }

        return new FontSource(kind, value, format, tech.ToArray());
    }

    private static string? ParseLocalName(List<Tok> t)
    {
        if (t.Count == 1 && t[0].Kind == TokKind.String)
        {
            return t[0].Text;
        }

        if (t.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (Tok k in t)
        {
            if (k.Kind != TokKind.Ident)
            {
                return null;
            }

            parts.Add(k.Text);
        }

        return string.Join(' ', parts);
    }

    private bool ParseWeight(List<Tok> t)
    {
        if (t.Count == 1 && t[0].Kind == TokKind.Ident && t[0].Text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            WeightMin = WeightMax = 400;
            return true;
        }

        if (t.Count is < 1 or > 2 || !WeightValue(t[0], out float a) || (t.Count == 2 && !WeightValue(t[1], out _)))
        {
            return false;
        }

        float b = a;
        if (t.Count == 2)
        {
            WeightValue(t[1], out b);
        }

        // The user agent swaps a reversed range (CSS Fonts 4 §4.4).
        WeightMin = Math.Min(a, b);
        WeightMax = Math.Max(a, b);
        return true;
    }

    private static bool WeightValue(Tok k, out float v)
    {
        v = 0;
        if (k.Kind == TokKind.Number && k.Number >= 1 && k.Number <= 1000)
        {
            v = (float)k.Number;
            return true;
        }

        if (k.Kind == TokKind.Ident)
        {
            if (k.Text.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                v = 400;
                return true;
            }

            if (k.Text.Equals("bold", StringComparison.OrdinalIgnoreCase))
            {
                v = 700;
                return true;
            }
        }

        return false;
    }

    private bool ParseStyle(List<Tok> t)
    {
        if (t.Count == 0 || t[0].Kind != TokKind.Ident)
        {
            return false;
        }

        string w = t[0].Text;
        if (t.Count == 1 && (w.Equals("normal", StringComparison.OrdinalIgnoreCase) || w.Equals("auto", StringComparison.OrdinalIgnoreCase)))
        {
            Style = FontStyleKind.Normal;
            return true;
        }

        if (t.Count == 1 && w.Equals("italic", StringComparison.OrdinalIgnoreCase))
        {
            Style = FontStyleKind.Italic;
            return true;
        }

        if (w.Equals("oblique", StringComparison.OrdinalIgnoreCase) && t.Count <= 3)
        {
            float a = 14;
            float b = 14;
            if (t.Count >= 2 && !Angle(t[1], out a))
            {
                return false;
            }

            b = a;
            if (t.Count == 3 && !Angle(t[2], out b))
            {
                return false;
            }

            Style = FontStyleKind.Oblique;
            ObliqueMin = Math.Min(a, b);
            ObliqueMax = Math.Max(a, b);
            return true;
        }

        return false;
    }

    private static bool Angle(Tok k, out float degrees)
    {
        degrees = 0;
        if (k.Kind == TokKind.Number && k.Number == 0)
        {
            return true;
        }

        if (k.Kind != TokKind.Dimension || k.Unit is null)
        {
            return false;
        }

        double v = k.Number;
        if (k.Unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
        {
        }
        else if (k.Unit.Equals("grad", StringComparison.OrdinalIgnoreCase))
        {
            v = v * 0.9;
        }
        else if (k.Unit.Equals("rad", StringComparison.OrdinalIgnoreCase))
        {
            v = v * 180 / Math.PI;
        }
        else if (k.Unit.Equals("turn", StringComparison.OrdinalIgnoreCase))
        {
            v *= 360;
        }
        else
        {
            return false;
        }

        if (!(v >= -90 && v <= 90))
        {
            return false;
        }

        degrees = (float)v;
        return true;
    }

    private bool ParseStretch(List<Tok> t)
    {
        if (t.Count == 1 && t[0].Kind == TokKind.Ident)
        {
            if (t[0].Text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                StretchMin = StretchMax = 100;
                return true;
            }

            float k = StretchKeyword(t[0].Text);
            if (k > 0)
            {
                StretchMin = StretchMax = k;
                return true;
            }

            return false;
        }

        if (t.Count is < 1 or > 2 || t[0].Kind != TokKind.Percent || t[0].Number < 0 || (t.Count == 2 && (t[1].Kind != TokKind.Percent || t[1].Number < 0)))
        {
            return false;
        }

        float a = (float)t[0].Number;
        float b = t.Count == 2 ? (float)t[1].Number : a;
        StretchMin = Math.Min(a, b);
        StretchMax = Math.Max(a, b);
        return true;
    }

    /// <summary>The percentage a <c>font-stretch</c> keyword stands for (CSS Fonts 4 §2.4), or 0 when it is not one.</summary>
    /// <param name="keyword">An identifier such as <c>condensed</c>.</param>
    /// <returns>50 to 200, or 0.</returns>
    public static float StretchKeyword(string keyword)
    {
        return keyword.ToLowerInvariant() switch
        {
            "ultra-condensed" => 50,
            "extra-condensed" => 62.5f,
            "condensed" => 75,
            "semi-condensed" => 87.5f,
            "normal" => 100,
            "semi-expanded" => 112.5f,
            "expanded" => 125,
            "extra-expanded" => 150,
            "ultra-expanded" => 200,
            _ => 0,
        };
    }

    private bool ParseDisplay(List<Tok> t)
    {
        if (t.Count != 1 || t[0].Kind != TokKind.Ident)
        {
            return false;
        }

        switch (t[0].Text.ToLowerInvariant())
        {
            case "auto": Display = FontDisplay.Auto; return true;
            case "block": Display = FontDisplay.Block; return true;
            case "swap": Display = FontDisplay.Swap; return true;
            case "fallback": Display = FontDisplay.Fallback; return true;
            case "optional": Display = FontDisplay.Optional; return true;
            default: return false;
        }
    }

    private bool ParseRanges(List<Tok> t)
    {
        var ranges = new List<CodePointRange>();
        bool expectRange = true;
        foreach (Tok k in t)
        {
            if (expectRange)
            {
                if (k.Kind != TokKind.UnicodeRange || !ParseRange(k.Text, out CodePointRange r))
                {
                    return false;
                }

                if (ranges.Count < MaxRanges)
                {
                    ranges.Add(r);
                }

                expectRange = false;
            }
            else
            {
                if (k.Kind != TokKind.Comma)
                {
                    return false;
                }

                expectRange = true;
            }
        }

        if (expectRange)
        {
            return false;
        }

        UnicodeRanges = ranges.ToArray();
        return true;
    }

    // "U+" then one of: hex{1,6}, hex-hex, or hex digits followed by 1 to 5 '?' (six places in all).
    private static bool ParseRange(string text, out CodePointRange range)
    {
        range = default;
        ReadOnlySpan<char> s = text.AsSpan(2);
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            if (!ParseHex(s[..dash], out int a) || !ParseHex(s[(dash + 1)..], out int b) || a > b || b > 0x10FFFF)
            {
                return false;
            }

            range = new CodePointRange(a, b);
            return true;
        }

        int q = s.IndexOf('?');
        if (q < 0)
        {
            if (!ParseHex(s, out int a) || a > 0x10FFFF)
            {
                return false;
            }

            range = new CodePointRange(a, a);
            return true;
        }

        if (s.Length > 6)
        {
            return false;
        }

        for (int i = q; i < s.Length; i++)
        {
            if (s[i] != '?')
            {
                return false;
            }
        }

        int lo = 0;
        int hi = 0;
        for (int i = 0; i < s.Length; i++)
        {
            lo <<= 4;
            hi <<= 4;
            if (s[i] == '?')
            {
                hi |= 0xF;
            }
            else
            {
                int v = CssValueTokenizer.HexValue(s[i]);
                lo |= v;
                hi |= v;
            }
        }

        if (hi > 0x10FFFF)
        {
            return false;
        }

        range = new CodePointRange(lo, hi);
        return true;
    }

    private static bool ParseHex(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        if (s.Length is < 1 or > 6)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!(c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
            {
                return false;
            }

            value = (value * 16) + CssValueTokenizer.HexValue(c);
        }

        return true;
    }
}

/// <summary>Parsers for font property values that a host may hold as CSS text.</summary>
public static class FontCss
{
    /// <summary>
    /// Parses a <c>font-family</c> value: a comma-separated list of quoted or unquoted names and generic families.
    /// </summary>
    /// <param name="value">The text, for example <c>"Helvetica Neue", Arial, sans-serif</c>.</param>
    /// <param name="families">The entries; empty when the value is invalid.</param>
    /// <returns>False when the value is not a valid list (an empty entry, a stray token, more than <see cref="FontRequest.MaxFamilies"/> entries).</returns>
    public static bool TryParseFamilyList(string? value, out FontFamilyName[] families)
    {
        families = [];
        var tokens = new List<Tok>();
        if (value is null || !CssValueTokenizer.TryTokenize(value.AsSpan(), tokens) || tokens.Count == 0)
        {
            return false;
        }

        var result = new List<FontFamilyName>();
        var words = new List<string>();
        bool quoted = false;
        for (int i = 0; i <= tokens.Count; i++)
        {
            if (i == tokens.Count || tokens[i].Kind == TokKind.Comma)
            {
                if (words.Count == 0 || result.Count >= FontRequest.MaxFamilies)
                {
                    return false;
                }

                if (!quoted && words.Count == 1 && FontFamilyName.GenericFromKeyword(words[0]) is GenericFamily g and not GenericFamily.None)
                {
                    result.Add(FontFamilyName.OfGeneric(g));
                }
                else if (!quoted && words.Count == 1 && (words[0].Equals("inherit", StringComparison.OrdinalIgnoreCase) || words[0].Equals("initial", StringComparison.OrdinalIgnoreCase)
                    || words[0].Equals("unset", StringComparison.OrdinalIgnoreCase) || words[0].Equals("default", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
                else
                {
                    result.Add(new FontFamilyName(string.Join(' ', words)));
                }

                words.Clear();
                quoted = false;
                continue;
            }

            Tok t = tokens[i];
            if (t.Kind == TokKind.String && words.Count == 0)
            {
                words.Add(t.Text);
                quoted = true;
            }
            else if (t.Kind == TokKind.Ident && !quoted)
            {
                words.Add(t.Text);
            }
            else
            {
                return false;
            }
        }

        families = result.ToArray();
        return true;
    }

    /// <summary>Parses a <c>font-weight</c> value that is absolute: <c>normal</c>, <c>bold</c> or a number from 1 to 1000.</summary>
    /// <param name="value">The text.</param>
    /// <param name="weight">The weight.</param>
    /// <returns>False when the text is not an absolute weight.</returns>
    public static bool TryParseWeight(string? value, out float weight)
    {
        weight = 400;
        var tokens = new List<Tok>();
        if (value is null || !CssValueTokenizer.TryTokenize(value.AsSpan(), tokens) || tokens.Count != 1)
        {
            return false;
        }

        Tok k = tokens[0];
        if (k.Kind == TokKind.Number && k.Number >= 1 && k.Number <= 1000)
        {
            weight = (float)k.Number;
            return true;
        }

        if (k.Kind == TokKind.Ident)
        {
            if (k.Text.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (k.Text.Equals("bold", StringComparison.OrdinalIgnoreCase))
            {
                weight = 700;
                return true;
            }
        }

        return false;
    }

    /// <summary>Parses a <c>font-stretch</c> value: a keyword or a percentage.</summary>
    /// <param name="value">The text.</param>
    /// <param name="percent">The stretch as a percentage of normal.</param>
    /// <returns>False when invalid.</returns>
    public static bool TryParseStretch(string? value, out float percent)
    {
        percent = 100;
        var tokens = new List<Tok>();
        if (value is null || !CssValueTokenizer.TryTokenize(value.AsSpan(), tokens) || tokens.Count != 1)
        {
            return false;
        }

        if (tokens[0].Kind == TokKind.Percent && tokens[0].Number >= 0)
        {
            percent = (float)tokens[0].Number;
            return percent > 0;
        }

        if (tokens[0].Kind == TokKind.Ident)
        {
            float k = FontFaceRule.StretchKeyword(tokens[0].Text);
            if (k > 0)
            {
                percent = k;
                return true;
            }
        }

        return false;
    }
}
