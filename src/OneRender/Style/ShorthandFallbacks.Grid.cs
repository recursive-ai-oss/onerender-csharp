using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>The grid shorthands the OneHtml cascade keeps whole: <c>grid-area</c>, <c>grid-row</c>, <c>grid-column</c>, <c>grid-template</c>, <c>grid</c> and the <c>place-*</c> shorthands.</summary>
internal static partial class ShorthandFallbacks
{
    /// <summary>Every shorthand this class expands, for the front end that reads declared shorthands from the cascade.</summary>
    public static readonly string[] AllNames =
    [
        "white-space", "border-radius", "overflow",
        "grid-area", "grid-row", "grid-column", "grid-template", "grid", "place-items", "place-self", "place-content",
    ];

    private static readonly string[] GridNames = ["grid-area", "grid-row", "grid-column", "grid-template", "grid", "place-items", "place-self", "place-content"];

    private static bool IsGridShorthand(string name)
    {
        foreach (string g in GridNames)
        {
            if (name.Equals(g, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ReadOnlySpan<PropertyId> GridLonghands(string name)
    {
        if (name.Equals("grid-area", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.GridRowStart, PropertyId.GridColumnStart, PropertyId.GridRowEnd, PropertyId.GridColumnEnd };
        }

        if (name.Equals("grid-row", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.GridRowStart, PropertyId.GridRowEnd };
        }

        if (name.Equals("grid-column", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.GridColumnStart, PropertyId.GridColumnEnd };
        }

        if (name.Equals("place-items", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignItems, PropertyId.JustifyItems };
        }

        if (name.Equals("place-self", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignSelf, PropertyId.JustifySelf };
        }

        if (name.Equals("place-content", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { PropertyId.AlignContent, PropertyId.JustifyContent };
        }

        return new[]
        {
            PropertyId.GridTemplateRows, PropertyId.GridTemplateColumns, PropertyId.GridTemplateAreas,
            PropertyId.GridAutoRows, PropertyId.GridAutoColumns, PropertyId.GridAutoFlow,
        };
    }

    private static bool ExpandGrid(string name, ComponentValueList value, List<(PropertyId Id, ComponentValueList Value)> output)
    {
        if (CssWideKeywords.Detect(value) is CssWideKeyword.Initial or CssWideKeyword.Inherit or CssWideKeyword.Unset or CssWideKeyword.Revert or CssWideKeyword.RevertLayer)
        {
            foreach (PropertyId id in GridLonghands(name))
            {
                output.Add((id, value));
            }

            return true;
        }

        // Tokens split into slash-separated groups; each group is a list of the raw text of its component values.
        var groups = new List<List<string>> { new() };
        foreach (ComponentValue cv in value)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Delim && cv.Raw.Length == 1 && cv.Raw[0] == '/')
            {
                groups.Add([]);
                continue;
            }

            groups[^1].Add(cv.Raw.ToString());
            if (groups[^1].Count > 4096 || groups.Count > 8)
            {
                return false;
            }
        }

        foreach (List<string> g in groups)
        {
            if (g.Count == 0)
            {
                return false;
            }
        }

        if (name.StartsWith("place-", StringComparison.OrdinalIgnoreCase))
        {
            return ExpandPlace(name, groups, output);
        }

        if (name.Equals("grid-area", StringComparison.OrdinalIgnoreCase))
        {
            if (groups.Count > 4)
            {
                return false;
            }

            string rs = string.Join(' ', groups[0]);
            string cs = groups.Count > 1 ? string.Join(' ', groups[1]) : IsCustomIdent(groups[0]) ? rs : "auto";
            string re = groups.Count > 2 ? string.Join(' ', groups[2]) : IsCustomIdent(groups[0]) ? rs : "auto";
            string ce = groups.Count > 3 ? string.Join(' ', groups[3]) : groups.Count > 1 && IsCustomIdent(groups[1]) ? cs : IsCustomIdent(groups[0]) && groups.Count == 1 ? rs : "auto";
            output.Add((PropertyId.GridRowStart, Text(rs)));
            output.Add((PropertyId.GridColumnStart, Text(cs)));
            output.Add((PropertyId.GridRowEnd, Text(re)));
            output.Add((PropertyId.GridColumnEnd, Text(ce)));
            return true;
        }

        if (name.Equals("grid-row", StringComparison.OrdinalIgnoreCase) || name.Equals("grid-column", StringComparison.OrdinalIgnoreCase))
        {
            if (groups.Count > 2)
            {
                return false;
            }

            string start = string.Join(' ', groups[0]);
            string end = groups.Count > 1 ? string.Join(' ', groups[1]) : IsCustomIdent(groups[0]) ? start : "auto";
            bool row = name.Equals("grid-row", StringComparison.OrdinalIgnoreCase);
            output.Add((row ? PropertyId.GridRowStart : PropertyId.GridColumnStart, Text(start)));
            output.Add((row ? PropertyId.GridRowEnd : PropertyId.GridColumnEnd, Text(end)));
            return true;
        }

        if (groups.Count > 2)
        {
            return false;
        }

        bool isGrid = name.Equals("grid", StringComparison.OrdinalIgnoreCase);
        if (isGrid && groups.Count == 2)
        {
            bool autoRows = IsAutoFlow(groups[0]);
            bool autoCols = IsAutoFlow(groups[1]);
            if (autoRows != autoCols)
            {
                // <rows> / auto-flow && dense? <auto-columns>?   or   auto-flow && dense? <auto-rows>? / <columns>
                List<string> flow = autoRows ? groups[0] : groups[1];
                List<string> tracks = autoRows ? groups[1] : groups[0];
                bool dense = false;
                var autoSizes = new List<string>();
                for (int i = 0; i < flow.Count; i++)
                {
                    if (flow[i].Equals("auto-flow", StringComparison.OrdinalIgnoreCase) && i == 0 || flow[i].Equals("auto-flow", StringComparison.OrdinalIgnoreCase) && i == 1 && flow[0].Equals("dense", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (flow[i].Equals("dense", StringComparison.OrdinalIgnoreCase) && !dense && (i == 0 || i == 1))
                    {
                        dense = true;
                        continue;
                    }

                    autoSizes.Add(flow[i]);
                }

                string autoText = autoSizes.Count == 0 ? "auto" : string.Join(' ', autoSizes);
                string trackText = string.Join(' ', tracks);
                output.Add((autoRows ? PropertyId.GridTemplateRows : PropertyId.GridTemplateColumns, Text("none")));
                output.Add((autoRows ? PropertyId.GridTemplateColumns : PropertyId.GridTemplateRows, Text(trackText)));
                output.Add((PropertyId.GridTemplateAreas, Text("none")));
                output.Add((autoRows ? PropertyId.GridAutoRows : PropertyId.GridAutoColumns, Text(autoText)));
                output.Add((autoRows ? PropertyId.GridAutoColumns : PropertyId.GridAutoRows, Text("auto")));
                output.Add((PropertyId.GridAutoFlow, Text((autoRows ? "row" : "column") + (dense ? " dense" : string.Empty))));
                return true;
            }
        }

        if (!ExpandTemplate(groups, output))
        {
            return false;
        }

        if (isGrid)
        {
            output.Add((PropertyId.GridAutoRows, Text("auto")));
            output.Add((PropertyId.GridAutoColumns, Text("auto")));
            output.Add((PropertyId.GridAutoFlow, Text("row")));
        }

        return true;
    }

    private static bool IsAutoFlow(List<string> g) => g[0].Equals("auto-flow", StringComparison.OrdinalIgnoreCase) || g.Count > 1 && g[0].Equals("dense", StringComparison.OrdinalIgnoreCase) && g[1].Equals("auto-flow", StringComparison.OrdinalIgnoreCase);

    private static bool IsCustomIdent(List<string> g) =>
        g.Count == 1 && g[0].Length > 0 && !char.IsAsciiDigit(g[0][0]) && g[0][0] is not ('-' or '+' or '.') && !g[0].Equals("auto", StringComparison.OrdinalIgnoreCase) && !g[0].Equals("span", StringComparison.OrdinalIgnoreCase);

    /// <summary>Grid 2 §7.4: <c>none</c>, <c>&lt;rows&gt; / &lt;columns&gt;</c>, or the "ASCII art" form with strings.</summary>
    private static bool ExpandTemplate(List<List<string>> groups, List<(PropertyId Id, ComponentValueList Value)> output)
    {
        List<string> first = groups[0];
        bool hasString = false;
        foreach (string t in first)
        {
            if (t.Length > 0 && t[0] is '"' or '\'')
            {
                hasString = true;
                break;
            }
        }

        if (!hasString)
        {
            if (groups.Count == 1)
            {
                if (first.Count != 1 || !first[0].Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                output.Add((PropertyId.GridTemplateRows, Text("none")));
                output.Add((PropertyId.GridTemplateColumns, Text("none")));
            }
            else
            {
                output.Add((PropertyId.GridTemplateRows, Text(string.Join(' ', first))));
                output.Add((PropertyId.GridTemplateColumns, Text(string.Join(' ', groups[1]))));
            }

            output.Add((PropertyId.GridTemplateAreas, Text("none")));
            return true;
        }

        // [ <line-names>? <string> <track-size>? <line-names>? ]+
        var areas = new System.Text.StringBuilder();
        var rows = new System.Text.StringBuilder();
        var pending = new List<string>();
        int i = 0;
        int count = 0;
        while (i < first.Count)
        {
            while (i < first.Count && first[i].StartsWith('['))
            {
                AddNames(pending, first[i++]);
            }

            if (i >= first.Count || first[i].Length == 0 || first[i][0] is not ('"' or '\''))
            {
                return false;
            }

            areas.Append(areas.Length > 0 ? " " : string.Empty).Append(first[i++]);
            AppendNames(rows, pending);
            string size = "auto";
            if (i < first.Count && !first[i].StartsWith('[') && first[i][0] is not ('"' or '\''))
            {
                size = first[i++];
            }

            rows.Append(rows.Length > 0 ? " " : string.Empty).Append(size);
            while (i < first.Count && first[i].StartsWith('['))
            {
                AddNames(pending, first[i++]);
            }

            if (++count > GridTrackList.MaxTracks)
            {
                return false;
            }
        }

        AppendNames(rows, pending);
        output.Add((PropertyId.GridTemplateAreas, Text(areas.ToString())));
        output.Add((PropertyId.GridTemplateRows, Text(rows.ToString())));
        output.Add((PropertyId.GridTemplateColumns, Text(groups.Count > 1 ? string.Join(' ', groups[1]) : "none")));
        return true;
    }

    private static void AddNames(List<string> pending, string block)
    {
        ReadOnlySpan<char> inner = block.AsSpan().Trim('[').Trim(']');
        foreach (Range r in inner.Split(' '))
        {
            ReadOnlySpan<char> w = inner[r].Trim();
            if (!w.IsEmpty)
            {
                pending.Add(w.ToString());
            }
        }
    }

    private static void AppendNames(System.Text.StringBuilder sb, List<string> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        sb.Append(sb.Length > 0 ? " [" : "[").Append(string.Join(' ', pending)).Append(']');
        pending.Clear();
    }

    private static bool ExpandPlace(string name, List<List<string>> groups, List<(PropertyId Id, ComponentValueList Value)> output)
    {
        if (groups.Count != 1)
        {
            return false;
        }

        // One or two alignment values; "first baseline", "last baseline", "safe x" and "unsafe x" are two words each.
        List<string> words = groups[0];
        var values = new List<string>(2);
        int i = 0;
        while (i < words.Count)
        {
            string w = words[i];
            bool modifier = w.Equals("first", StringComparison.OrdinalIgnoreCase) || w.Equals("last", StringComparison.OrdinalIgnoreCase)
                || w.Equals("safe", StringComparison.OrdinalIgnoreCase) || w.Equals("unsafe", StringComparison.OrdinalIgnoreCase);
            if (modifier)
            {
                if (i + 1 >= words.Count)
                {
                    return false;
                }

                values.Add(w + " " + words[i + 1]);
                i += 2;
            }
            else
            {
                values.Add(w);
                i++;
            }
        }

        if (values.Count is < 1 or > 2)
        {
            return false;
        }

        string a = values[0], j = values.Count > 1 ? values[1] : values[0];
        if (name.Equals("place-items", StringComparison.OrdinalIgnoreCase))
        {
            output.Add((PropertyId.AlignItems, Text(a)));
            output.Add((PropertyId.JustifyItems, Text(j)));
        }
        else if (name.Equals("place-self", StringComparison.OrdinalIgnoreCase))
        {
            output.Add((PropertyId.AlignSelf, Text(a)));
            output.Add((PropertyId.JustifySelf, Text(j)));
        }
        else
        {
            output.Add((PropertyId.AlignContent, Text(a)));
            output.Add((PropertyId.JustifyContent, Text(j)));
        }

        return true;
    }
}
