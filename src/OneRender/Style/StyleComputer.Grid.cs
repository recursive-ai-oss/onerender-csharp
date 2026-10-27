using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

internal sealed partial class StyleComputer
{
    private sealed class Seg
    {
        public readonly List<GridTrackSize> S = [];
        public readonly List<string[]?> N = [null];

        public void AddNames(string[] names)
        {
            string[]? cur = N[^1];
            if (cur is null)
            {
                N[^1] = names;
                return;
            }

            var merged = new string[cur.Length + names.Length];
            cur.CopyTo(merged, 0);
            names.CopyTo(merged, cur.Length);
            N[^1] = merged;
        }

        public void AddTrack(GridTrackSize t)
        {
            S.Add(t);
            N.Add(null);
        }

        public void Append(Seg other, int times, ref int budget)
        {
            for (int k = 0; k < times && other.S.Count <= budget; k++)
            {
                if (other.N[0] is { } first)
                {
                    AddNames(first);
                }

                for (int i = 0; i < other.S.Count; i++)
                {
                    S.Add(other.S[i]);
                    N.Add(other.N[i + 1]);
                }

                budget -= other.S.Count;
                if (other.S.Count == 0)
                {
                    break;
                }
            }
        }
    }

    private bool ApplyGrid(PropertyId id, ComponentValueList v)
    {
        switch (id)
        {
            case PropertyId.GridTemplateColumns:
            case PropertyId.GridTemplateRows:
            {
                if (!ParseTrackList(v, out GridTrackList list))
                {
                    return false;
                }

                if (id == PropertyId.GridTemplateColumns)
                {
                    _r.GridTemplateColumns = list;
                }
                else
                {
                    _r.GridTemplateRows = list;
                }

                return true;
            }

            case PropertyId.GridTemplateAreas:
                return ParseGridAreas(v, out GridAreas areas) && SetAreas(areas);
            case PropertyId.GridAutoColumns:
            case PropertyId.GridAutoRows:
            {
                var sizes = new List<GridTrackSize>();
                foreach (ComponentValue p in v)
                {
                    if (p.IsWhitespace)
                    {
                        continue;
                    }

                    if (sizes.Count >= 1024 || !ParseTrackSize(p, true, out GridTrackSize t))
                    {
                        return false;
                    }

                    sizes.Add(t);
                }

                if (sizes.Count == 0)
                {
                    return false;
                }

                if (id == PropertyId.GridAutoColumns)
                {
                    _r.GridAutoColumns = sizes.ToArray();
                }
                else
                {
                    _r.GridAutoRows = sizes.ToArray();
                }

                return true;
            }

            case PropertyId.GridAutoFlow:
            {
                int n = SplitParts(v);
                if (n is < 1 or > 2)
                {
                    return false;
                }

                bool dense = false, haveAxis = false;
                var axis = GridAutoFlowKind.Row;
                for (int i = 0; i < n; i++)
                {
                    if (!IsIdentPart(_parts[i], out ReadOnlySpan<char> t))
                    {
                        return false;
                    }

                    if (t.Equals("dense", StringComparison.OrdinalIgnoreCase) && !dense)
                    {
                        dense = true;
                    }
                    else if (t.Equals("row", StringComparison.OrdinalIgnoreCase) && !haveAxis)
                    {
                        haveAxis = true;
                    }
                    else if (t.Equals("column", StringComparison.OrdinalIgnoreCase) && !haveAxis)
                    {
                        haveAxis = true;
                        axis = GridAutoFlowKind.Column;
                    }
                    else
                    {
                        return false;
                    }
                }

                _r.GridAutoFlow = axis;
                _r.GridAutoFlowDense = dense;
                return true;
            }

            case PropertyId.GridRowStart:
            case PropertyId.GridRowEnd:
            case PropertyId.GridColumnStart:
            case PropertyId.GridColumnEnd:
            {
                if (!ParseGridLine(v, out GridLine line))
                {
                    return false;
                }

                switch (id)
                {
                    case PropertyId.GridRowStart: _r.GridRowStart = line; break;
                    case PropertyId.GridRowEnd: _r.GridRowEnd = line; break;
                    case PropertyId.GridColumnStart: _r.GridColumnStart = line; break;
                    default: _r.GridColumnEnd = line; break;
                }

                return true;
            }

            case PropertyId.JustifyItems:
            {
                if (IsWord(v, "legacy"))
                {
                    _r.JustifyItems = AlignKind.Normal;
                    _r.JustifyItemsOverflow = AlignOverflowKind.Default;
                    return true;
                }

                if (!ParseAlign(v, content: false, self: false, out AlignKind k, out AlignOverflowKind o))
                {
                    return false;
                }

                _r.JustifyItems = k;
                _r.JustifyItemsOverflow = o;
                return true;
            }

            case PropertyId.JustifySelf:
            {
                if (!ParseAlign(v, content: false, self: true, out AlignKind k, out AlignOverflowKind o))
                {
                    return false;
                }

                _r.JustifySelf = k;
                _r.JustifySelfOverflow = o;
                return true;
            }

            default:
                return false;
        }
    }

    private bool SetAreas(GridAreas a)
    {
        _r.GridTemplateAreas = a;
        return true;
    }

    private static void CopyGridProperty(PropertyId id, ref ComputedValues d, in ComputedValues s)
    {
        switch (id)
        {
            case PropertyId.GridTemplateColumns: d.GridTemplateColumns = s.GridTemplateColumns; break;
            case PropertyId.GridTemplateRows: d.GridTemplateRows = s.GridTemplateRows; break;
            case PropertyId.GridTemplateAreas: d.GridTemplateAreas = s.GridTemplateAreas; break;
            case PropertyId.GridAutoColumns: d.GridAutoColumns = s.GridAutoColumns; break;
            case PropertyId.GridAutoRows: d.GridAutoRows = s.GridAutoRows; break;
            case PropertyId.GridAutoFlow: d.GridAutoFlow = s.GridAutoFlow; d.GridAutoFlowDense = s.GridAutoFlowDense; break;
            case PropertyId.GridRowStart: d.GridRowStart = s.GridRowStart; break;
            case PropertyId.GridRowEnd: d.GridRowEnd = s.GridRowEnd; break;
            case PropertyId.GridColumnStart: d.GridColumnStart = s.GridColumnStart; break;
            case PropertyId.GridColumnEnd: d.GridColumnEnd = s.GridColumnEnd; break;
            case PropertyId.JustifyItems: d.JustifyItems = s.JustifyItems; d.JustifyItemsOverflow = s.JustifyItemsOverflow; break;
            case PropertyId.JustifySelf: d.JustifySelf = s.JustifySelf; d.JustifySelfOverflow = s.JustifySelfOverflow; break;
        }
    }

    // ---- track sizes ----

    private bool ParseBreadth(ComponentValue part, bool allowFlex, out GridBreadth b)
    {
        b = GridBreadth.Auto;
        if (part.Kind == ComponentValueKind.Token)
        {
            switch (part.Token.Kind)
            {
                case CssTokenKind.Ident:
                {
                    ReadOnlySpan<char> t = part.Raw;
                    if (t.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (t.Equals("min-content", StringComparison.OrdinalIgnoreCase))
                    {
                        b = new GridBreadth(GridBreadthKind.MinContent, default, 0);
                        return true;
                    }

                    if (t.Equals("max-content", StringComparison.OrdinalIgnoreCase))
                    {
                        b = new GridBreadth(GridBreadthKind.MaxContent, default, 0);
                        return true;
                    }

                    return false;
                }

                case CssTokenKind.Dimension:
                {
                    ReadOnlySpan<char> raw = part.Raw;
                    ReadOnlySpan<char> unit = raw[part.Token.NumberText.Length..];
                    if (unit.Equals("fr", StringComparison.OrdinalIgnoreCase))
                    {
                        double f = part.Token.NumberValue;
                        if (!allowFlex || !(f >= 0) || double.IsInfinity(f))
                        {
                            return false;
                        }

                        b = new GridBreadth(GridBreadthKind.Flex, default, f);
                        return true;
                    }

                    break;
                }

                case CssTokenKind.Number when part.Token.NumberValue == 0:
                    // CSS Values 4 §5.1: a unitless zero is a valid <length>.
                    b = new GridBreadth(GridBreadthKind.Length, Length.Zero, 0);
                    return true;
            }
        }

        if (!Len(ListOf(part), LengthAllow.Percent | LengthAllow.NonNegative, out Length l) || l.Kind is not (LengthKind.Px or LengthKind.Percent or LengthKind.Calc))
        {
            return false;
        }

        b = new GridBreadth(GridBreadthKind.Length, l, 0);
        return true;
    }

    private bool ParseTrackSize(ComponentValue part, bool allowFlex, out GridTrackSize t)
    {
        t = GridTrackSize.Auto;
        if (part.Kind == ComponentValueKind.Function)
        {
            if (part.IsFunctionNamed("minmax"))
            {
                var args = new List<ComponentValue>(4);
                if (!SplitCommaArgs(part.Children, args, 2) || args.Count != 2)
                {
                    return false;
                }

                if (!ParseBreadth(args[0], false, out GridBreadth min) || !ParseBreadth(args[1], allowFlex, out GridBreadth max))
                {
                    return false;
                }

                // Grid 2 §7.2.1: if the max is less than the min the max is floored by it; layout does that with the resolved sizes.
                t = new GridTrackSize(min, max);
                return true;
            }

            if (part.IsFunctionNamed("fit-content"))
            {
                if (!Len(part.Children, LengthAllow.Percent | LengthAllow.NonNegative, out Length l) || l.Kind is not (LengthKind.Px or LengthKind.Percent or LengthKind.Calc))
                {
                    return false;
                }

                t = new GridTrackSize(GridBreadth.Auto, new GridBreadth(GridBreadthKind.FitContent, l, 0));
                return true;
            }
        }

        if (!ParseBreadth(part, allowFlex, out GridBreadth b))
        {
            return false;
        }

        // A bare flexible length implies an auto minimum; every other bare breadth is both minimum and maximum.
        t = b.Kind == GridBreadthKind.Flex ? new GridTrackSize(GridBreadth.Auto, b) : new GridTrackSize(b, b);
        return true;
    }

    private static bool SplitCommaArgs(ComponentValueList list, List<ComponentValue> parts, int max)
    {
        // Collects the non-whitespace component values; commas separate groups, each of which must be one value here.
        int groups = 1;
        bool have = false;
        foreach (ComponentValue cv in list)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Comma)
            {
                if (!have || ++groups > max)
                {
                    return false;
                }

                have = false;
                continue;
            }

            if (have)
            {
                return false;
            }

            parts.Add(cv);
            have = true;
        }

        return have;
    }

    private static bool IsFixedSize(in GridTrackSize t) => t.Min.Kind == GridBreadthKind.Length || t.Max.Kind == GridBreadthKind.Length;

    // ---- track lists ----

    private bool ParseTrackList(ComponentValueList v, out GridTrackList list)
    {
        list = GridTrackList.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        bool subgrid = false, first = true, haveAuto = false, autoFit = false;
        var head = new Seg();
        var tail = new Seg();
        var auto = new Seg();
        Seg cur = head;
        int budget = GridTrackList.MaxTracks;
        foreach (ComponentValue part in v)
        {
            if (part.IsWhitespace)
            {
                continue;
            }

            if (first)
            {
                first = false;
                if (part.Kind == ComponentValueKind.Token && part.Token.Kind == CssTokenKind.Ident && part.Raw.Equals("subgrid", StringComparison.OrdinalIgnoreCase))
                {
                    subgrid = true;
                    head.N.Clear();
                    continue;
                }
            }

            if (part.Kind == ComponentValueKind.SimpleBlock)
            {
                if (!ParseLineNames(part, out string[] names))
                {
                    return false;
                }

                if (subgrid)
                {
                    if (budget-- <= 0)
                    {
                        continue;
                    }

                    cur.N.Add(names);
                }
                else
                {
                    cur.AddNames(names);
                }

                continue;
            }

            if (part.IsFunctionNamed("repeat"))
            {
                if (!ParseRepeat(part, subgrid, haveAuto, out int count, out bool isAuto, out bool isFit, out Seg inner))
                {
                    return false;
                }

                if (isAuto)
                {
                    haveAuto = true;
                    autoFit = isFit;
                    foreach (GridTrackSize t in inner.S)
                    {
                        if (!IsFixedSize(in t))
                        {
                            return false;
                        }
                    }

                    auto = inner;
                    cur = tail;
                    if (subgrid)
                    {
                        tail.N.Clear();
                    }
                }
                else if (subgrid)
                {
                    for (int k = 0; k < count && budget > 0; k++)
                    {
                        foreach (string[]? n in inner.N)
                        {
                            if (budget-- > 0)
                            {
                                cur.N.Add(n);
                            }
                        }
                    }
                }
                else
                {
                    cur.Append(inner, count, ref budget);
                }

                continue;
            }

            if (subgrid || !ParseTrackSize(part, true, out GridTrackSize size))
            {
                return false;
            }

            if (budget > 0)
            {
                cur.AddTrack(size);
                budget--;
            }
        }

        if (subgrid)
        {
            if (!haveAuto)
            {
                tail.N.Clear();
            }

            list = new GridTrackList(true, [], head.N.ToArray(), [], auto.N.ToArray(), false, [], tail.N.ToArray(), haveAuto);
            return true;
        }

        if (head.S.Count + auto.S.Count + tail.S.Count == 0)
        {
            return false;
        }

        if (haveAuto)
        {
            foreach (GridTrackSize t in head.S)
            {
                if (!IsFixedSize(in t))
                {
                    return false;
                }
            }

            foreach (GridTrackSize t in tail.S)
            {
                if (!IsFixedSize(in t))
                {
                    return false;
                }
            }
        }

        list = new GridTrackList(false, head.S.ToArray(), head.N.ToArray(), auto.S.ToArray(), auto.N.ToArray(), autoFit, tail.S.ToArray(), tail.N.ToArray(), haveAuto);
        return true;
    }

    private static bool ParseLineNames(ComponentValue block, out string[] names)
    {
        names = [];
        if (!block.Token.Raw.SequenceEqual("[") )
        {
            return false;
        }

        var list = new List<string>(2);
        foreach (ComponentValue cv in block.Children)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind != ComponentValueKind.Token || cv.Token.Kind != CssTokenKind.Ident)
            {
                return false;
            }

            string name = cv.Token.Value;
            if (name.Equals("span", StringComparison.OrdinalIgnoreCase) || name.Equals("auto", StringComparison.OrdinalIgnoreCase) || IsCssWide(name))
            {
                return false;
            }

            list.Add(name);
            if (list.Count > 256)
            {
                return false;
            }
        }

        names = list.ToArray();
        return true;
    }

    private static bool IsCssWide(string s) =>
        s.Equals("inherit", StringComparison.OrdinalIgnoreCase) || s.Equals("initial", StringComparison.OrdinalIgnoreCase) || s.Equals("unset", StringComparison.OrdinalIgnoreCase)
        || s.Equals("default", StringComparison.OrdinalIgnoreCase);

    /// <summary>Grid 2 §7.2.3.1: repeat( [ &lt;integer [1,∞]&gt; | auto-fill | auto-fit ] , &lt;track-list&gt; ); in a subgrid, only line names are repeated.</summary>
    private bool ParseRepeat(ComponentValue fn, bool subgrid, bool haveAuto, out int count, out bool isAuto, out bool isFit, out Seg inner)
    {
        count = 1;
        isAuto = isFit = false;
        inner = new Seg();
        ComponentValueList args = fn.Children;
        bool seenComma = false;
        bool haveCount = false;
        foreach (ComponentValue cv in args)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (!seenComma)
            {
                if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Comma)
                {
                    if (!haveCount)
                    {
                        return false;
                    }

                    seenComma = true;
                    continue;
                }

                if (haveCount)
                {
                    return false;
                }

                if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Ident)
                {
                    if (cv.Raw.Equals("auto-fill", StringComparison.OrdinalIgnoreCase))
                    {
                        isAuto = true;
                    }
                    else if (cv.Raw.Equals("auto-fit", StringComparison.OrdinalIgnoreCase) && !subgrid)
                    {
                        isAuto = isFit = true;
                    }
                    else
                    {
                        return false;
                    }

                    if (haveAuto)
                    {
                        return false;
                    }
                }
                else if (cv.Kind == ComponentValueKind.Token && cv.Token.Kind == CssTokenKind.Number && cv.Token.IsInteger && cv.Token.NumberValue >= 1)
                {
                    count = (int)Math.Min(cv.Token.NumberValue, GridTrackList.MaxTracks + 1.0);
                }
                else
                {
                    return false;
                }

                haveCount = true;
                continue;
            }

            if (cv.Kind == ComponentValueKind.SimpleBlock)
            {
                if (!ParseLineNames(cv, out string[] names))
                {
                    return false;
                }

                if (subgrid)
                {
                    inner.N.Add(names);
                }
                else
                {
                    inner.AddNames(names);
                }

                continue;
            }

            if (subgrid || !ParseTrackSize(cv, true, out GridTrackSize t))
            {
                return false;
            }

            if (inner.S.Count >= GridTrackList.MaxTracks)
            {
                return false;
            }

            inner.AddTrack(t);
        }

        if (!seenComma)
        {
            return false;
        }

        if (subgrid)
        {
            // The first set of a name list belongs to the line the list starts at: the N list was seeded with one empty set.
            inner.N.RemoveAt(0);
            return inner.N.Count >= 1;
        }

        return inner.S.Count >= 1;
    }

    // ---- grid-template-areas ----

    private static bool ParseGridAreas(ComponentValueList v, out GridAreas areas)
    {
        areas = GridAreas.None;
        if (IsWord(v, "none"))
        {
            return true;
        }

        var rows = new List<string>();
        foreach (ComponentValue cv in v)
        {
            if (cv.IsWhitespace)
            {
                continue;
            }

            if (cv.Kind != ComponentValueKind.Token || cv.Token.Kind != CssTokenKind.QuotedString || rows.Count >= GridTrackList.MaxTracks)
            {
                return false;
            }

            rows.Add(cv.Token.Value);
        }

        if (rows.Count == 0)
        {
            return false;
        }

        var cells = new List<string?>();
        int columns = -1;
        for (int r = 0; r < rows.Count; r++)
        {
            int before = cells.Count;
            string s = rows[r];
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    i++;
                }
                else if (c == '.')
                {
                    while (i < s.Length && s[i] == '.')
                    {
                        i++;
                    }

                    cells.Add(null);
                }
                else if (IsIdentChar(c))
                {
                    int st = i;
                    while (i < s.Length && IsIdentChar(s[i]))
                    {
                        i++;
                    }

                    cells.Add(s.Substring(st, i - st));
                }
                else
                {
                    return false;
                }

                if (cells.Count - before > GridTrackList.MaxTracks)
                {
                    return false;
                }
            }

            int n = cells.Count - before;
            if (n == 0 || (columns >= 0 && n != columns))
            {
                return false;
            }

            columns = n;
        }

        var names = new List<string>();
        var rs = new List<int>();
        var re = new List<int>();
        var cs = new List<int>();
        var ce = new List<int>();
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                string? name = cells[r * columns + c];
                if (name is null)
                {
                    continue;
                }

                int idx = names.IndexOf(name);
                if (idx < 0)
                {
                    names.Add(name);
                    rs.Add(r + 1);
                    re.Add(r + 2);
                    cs.Add(c + 1);
                    ce.Add(c + 2);
                }
                else
                {
                    rs[idx] = Math.Min(rs[idx], r + 1);
                    re[idx] = Math.Max(re[idx], r + 2);
                    cs[idx] = Math.Min(cs[idx], c + 1);
                    ce[idx] = Math.Max(ce[idx], c + 2);
                }
            }
        }

        // Every named area must fill its bounding rectangle (Grid 2 §7.3).
        for (int k = 0; k < names.Count; k++)
        {
            for (int r = rs[k] - 1; r < re[k] - 1; r++)
            {
                for (int c = cs[k] - 1; c < ce[k] - 1; c++)
                {
                    if (!string.Equals(cells[r * columns + c], names[k], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append(r == 0 ? "\"" : " \"");
            for (int c = 0; c < columns; c++)
            {
                sb.Append(c == 0 ? string.Empty : " ").Append(cells[r * columns + c] ?? ".");
            }

            sb.Append('"');
        }

        areas = new GridAreas(rows.Count, columns, names.ToArray(), rs.ToArray(), re.ToArray(), cs.ToArray(), ce.ToArray(), sb.ToString());
        return true;
    }

    private static bool IsIdentChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 0x7F;

    // ---- grid lines ----

    private bool ParseGridLine(ComponentValueList v, out GridLine line)
    {
        line = GridLine.Auto;
        int n = SplitParts(v);
        if (n is < 1 or > 3)
        {
            return false;
        }

        bool span = false;
        int number = 0;
        bool haveNumber = false;
        string? name = null;
        for (int i = 0; i < n; i++)
        {
            ComponentValue p = _parts[i];
            if (p.Kind != ComponentValueKind.Token)
            {
                return false;
            }

            if (p.Token.Kind == CssTokenKind.Ident)
            {
                string t = p.Token.Value;
                if (t.Equals("span", StringComparison.OrdinalIgnoreCase))
                {
                    if (span)
                    {
                        return false;
                    }

                    span = true;
                }
                else if (t.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (n != 1)
                    {
                        return false;
                    }

                    return true;
                }
                else if (IsCssWide(t) || name is not null)
                {
                    return false;
                }
                else
                {
                    name = t;
                }
            }
            else if (p.Token.Kind == CssTokenKind.Number && p.Token.IsInteger && !haveNumber)
            {
                double d = p.Token.NumberValue;
                if (d == 0 || double.IsNaN(d))
                {
                    return false;
                }

                number = (int)Math.Clamp(d, -1_000_000, 1_000_000);
                haveNumber = true;
            }
            else
            {
                return false;
            }
        }

        if (span)
        {
            if (haveNumber && number < 1 || (!haveNumber && name is null))
            {
                return false;
            }

            line = new GridLine(GridLineKind.Span, haveNumber ? number : 1, name);
            return true;
        }

        if (!haveNumber && name is null)
        {
            return false;
        }

        line = new GridLine(GridLineKind.Line, number, name);
        return true;
    }
}
