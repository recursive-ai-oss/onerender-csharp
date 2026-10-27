using OneFont;

namespace OneRender.Text;

// CSS Fonts 4 §6 (font-variant-*) and §7 (font-feature-settings): the properties that choose OpenType features, reduced to the
// ordered feature list the shaper takes. An invalid property value is ignored whole, as CSS says for an invalid declaration.
internal static class FontVariantParser
{
    public static FontFeatureSettings Build(string? kerning, string? ligatures, string? caps, string? numeric, string? eastAsian, string? position, string? featureSettings)
    {
        var features = new List<FontFeature>();
        KerningMode mode = KerningMode.Normal;
        if (!string.IsNullOrWhiteSpace(kerning) && kerning.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            mode = KerningMode.None;
        }

        Variant(ligatures, features, LigatureMap);
        Variant(caps, features, CapsMap);
        Variant(numeric, features, NumericMap);
        Variant(eastAsian, features, EastAsianMap);
        Variant(position, features, PositionMap);
        FeatureSettingsList(featureSettings, features);
        if (features.Count == 0 && mode == KerningMode.Normal)
        {
            return FontFeatureSettings.Default;
        }

        return new FontFeatureSettings(features.ToArray(), mode);
    }

    // keyword -> features it sets
    private static readonly (string Keyword, string Tags, int Value)[] LigatureMap =
    [
        ("common-ligatures", "liga clig", 1),
        ("no-common-ligatures", "liga clig", 0),
        ("discretionary-ligatures", "dlig", 1),
        ("no-discretionary-ligatures", "dlig", 0),
        ("historical-ligatures", "hlig", 1),
        ("no-historical-ligatures", "hlig", 0),
        ("contextual", "calt", 1),
        ("no-contextual", "calt", 0),
    ];

    private static readonly (string Keyword, string Tags, int Value)[] CapsMap =
    [
        ("small-caps", "smcp", 1),
        ("all-small-caps", "smcp c2sc", 1),
        ("petite-caps", "pcap", 1),
        ("all-petite-caps", "pcap c2pc", 1),
        ("unicase", "unic", 1),
        ("titling-caps", "titl", 1),
    ];

    private static readonly (string Keyword, string Tags, int Value)[] NumericMap =
    [
        ("lining-nums", "lnum", 1),
        ("oldstyle-nums", "onum", 1),
        ("proportional-nums", "pnum", 1),
        ("tabular-nums", "tnum", 1),
        ("diagonal-fractions", "frac", 1),
        ("stacked-fractions", "afrc", 1),
        ("ordinal", "ordn", 1),
        ("slashed-zero", "zero", 1),
    ];

    private static readonly (string Keyword, string Tags, int Value)[] EastAsianMap =
    [
        ("jis78", "jp78", 1),
        ("jis83", "jp83", 1),
        ("jis90", "jp90", 1),
        ("jis04", "jp04", 1),
        ("simplified", "smpl", 1),
        ("traditional", "trad", 1),
        ("full-width", "fwid", 1),
        ("proportional-width", "pwid", 1),
        ("ruby", "ruby", 1),
    ];

    private static readonly (string Keyword, string Tags, int Value)[] PositionMap =
    [
        ("sub", "subs", 1),
        ("super", "sups", 1),
    ];

    private static void Variant(string? text, List<FontFeature> into, (string Keyword, string Tags, int Value)[] map)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var tokens = new List<Tok>();
        if (!CssValueTokenizer.TryTokenize(text.AsSpan(), tokens))
        {
            return;
        }

        var picked = new List<FontFeature>();
        foreach (Tok t in tokens)
        {
            if (t.Kind != TokKind.Ident)
            {
                return;
            }

            if (t.Text.Equals("normal", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Count != 1)
                {
                    return;
                }

                continue;
            }

            if (t.Text.Equals("none", StringComparison.OrdinalIgnoreCase) && ReferenceEquals(map, LigatureMap))
            {
                if (tokens.Count != 1)
                {
                    return;
                }

                foreach (string tag in new[] { "liga", "clig", "dlig", "hlig", "calt" })
                {
                    picked.Add(new FontFeature(tag, 0));
                }

                continue;
            }

            bool found = false;
            foreach ((string keyword, string tags, int value) in map)
            {
                if (t.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string tag in tags.Split(' '))
                    {
                        picked.Add(new FontFeature(tag, value));
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }
        }

        into.AddRange(picked);
    }

    // normal | <feature-tag-value>#   where <feature-tag-value> = <string> [<integer [0,∞]> | on | off]?
    private static void FeatureSettingsList(string? text, List<FontFeature> into)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var tokens = new List<Tok>();
        if (!CssValueTokenizer.TryTokenize(text.AsSpan(), tokens) || tokens.Count == 0)
        {
            return;
        }

        if (tokens.Count == 1 && tokens[0].Kind == TokKind.Ident && tokens[0].Text.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var picked = new List<FontFeature>();
        int i = 0;
        while (i < tokens.Count)
        {
            if (tokens[i].Kind != TokKind.String)
            {
                return;
            }

            string tag = tokens[i].Text;
            if (tag.Length != 4)
            {
                return;
            }

            foreach (char c in tag)
            {
                if (c < 0x20 || c > 0x7E)
                {
                    return;
                }
            }

            i++;
            int value = 1;
            if (i < tokens.Count && tokens[i].Kind == TokKind.Number && tokens[i].Number >= 0 && tokens[i].Number == Math.Floor(tokens[i].Number) && tokens[i].Number < 65536)
            {
                value = (int)tokens[i].Number;
                i++;
            }
            else if (i < tokens.Count && tokens[i].Kind == TokKind.Ident && tokens[i].Text.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                i++;
            }
            else if (i < tokens.Count && tokens[i].Kind == TokKind.Ident && tokens[i].Text.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                value = 0;
                i++;
            }

            picked.Add(new FontFeature(tag, value));
            if (i < tokens.Count)
            {
                if (tokens[i].Kind != TokKind.Comma || i + 1 >= tokens.Count)
                {
                    return;
                }

                i++;
            }
        }

        into.AddRange(picked);
    }
}
