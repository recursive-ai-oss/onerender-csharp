using OneRender.Style;

namespace OneRender.Layout;

/// <summary>Line names of one grid axis: for each name the ascending 0-based indexes of the explicit lines that carry it.</summary>
internal sealed class GridNameIndex
{
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private List<int>[] _lists = new List<int>[8];
    private int _used;

    public void Clear()
    {
        _suffixed.Clear();
        _ids.Clear();
        for (int i = 0; i < _used; i++)
        {
            _lists[i].Clear();
        }

        _used = 0;
    }

    /// <summary>Gives <paramref name="line"/> the name; a line named twice is recorded once.</summary>
    public void Add(string name, int line)
    {
        if (!_ids.TryGetValue(name, out int id))
        {
            if (_used == _lists.Length)
            {
                Array.Resize(ref _lists, _used * 2);
            }

            _lists[_used] ??= new List<int>(4);
            id = _used++;
            _ids[name] = id;
        }

        List<int> l = _lists[id];
        int pos = l.BinarySearch(line);
        if (pos < 0)
        {
            l.Insert(~pos, line);
        }

        // Grid 2 §8.3: "foo" in a placement property first looks for a line named "foo-start" / "foo-end".
        if (name.EndsWith("-start", StringComparison.Ordinal))
        {
            Suffixed(name.Substring(0, name.Length - 6), "-start", line);
        }
        else if (name.EndsWith("-end", StringComparison.Ordinal))
        {
            Suffixed(name.Substring(0, name.Length - 4), "-end", line);
        }
    }

    private void Suffixed(string area, string suffix, int line)
    {
        if (!_suffixed.TryGetValue((area, suffix), out int old) || line < old)
        {
            _suffixed[(area, suffix)] = line;
        }
    }

    public bool Has(string name) => _ids.ContainsKey(name);

    /// <summary>Copies the names of lines <paramref name="lo"/>..<paramref name="hi"/> (inclusive) to <paramref name="dst"/>, renumbered to start at 0.</summary>
    public void CopyRange(GridNameIndex dst, int lo, int hi)
    {
        foreach (KeyValuePair<string, int> kv in _ids)
        {
            List<int> l = _lists[kv.Value];
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i] >= lo && l[i] <= hi)
                {
                    dst.Add(kv.Key, l[i] - lo);
                }
            }
        }
    }

    public bool TryGet(string name, out List<int> lines)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            lines = _lists[id];
            return true;
        }

        lines = null!;
        return false;
    }

    /// <summary>The line carrying <c>name + suffix</c>, first one; -1 when none. Used for the implicit area names (<c>foo-start</c>, <c>foo-end</c>).</summary>
    public int FirstWithSuffix(string name, string suffix)
    {
        // Only the area names need this; the dictionary is keyed by the full name, so build it without allocating for the common
        // case by trying the exact-suffix names the caller registered.
        return _suffixed.TryGetValue((name, suffix), out int line) ? line : -1;
    }

    private readonly Dictionary<(string, string), int> _suffixed = new();

    public void AddArea(string area, int startLine, int endLine)
    {
        Add(area + "-start", startLine);
        Add(area + "-end", endLine);
    }

}

/// <summary>One grid item for placement: its placement properties in, its resolved grid area out.</summary>
internal struct GridPlaceItem
{
    public GridLine ColStart, ColEnd, RowStart, RowEnd;

    /// <summary>Resolved start track (0 = first track of the implicit grid) and span, per axis (0 columns, 1 rows).</summary>
    public int Pos0, Span0, Pos1, Span1;

    /// <summary>Whether the axis position was definite before auto-placement.</summary>
    public bool Def0, Def1;

    /// <summary>Set when the item was clamped to the grid's track limit.</summary>
    public bool Clamped;
}

/// <summary>Inputs of the placement algorithm that are not per item.</summary>
internal struct GridPlaceParams
{
    /// <summary>Tracks in the explicit grid, per axis.</summary>
    public int Explicit0, Explicit1;

    /// <summary>A subgridded axis has no implicit tracks: every area is clamped to the explicit grid (Grid 2 §9).</summary>
    public bool Sub0, Sub1;

    public bool ColumnFlow, Dense;
}

/// <summary>The grid item placement algorithm (CSS Grid 2 §8.3 and §8.5), with the limits of §5.4.</summary>
internal sealed class GridPlacer
{
    private ulong[] _bits = new ulong[64];
    private int _words;
    private int _rows;
    private int _cols;
    private long _work;

    /// <summary>The most work (cell visits) auto-placement does for one grid before it stacks the remaining items; keeps hostile grids linear.</summary>
    public const long MaxWork = 50_000_000;

    /// <summary>Largest occupancy matrix, in cells, auto-placement keeps.</summary>
    public const long MaxCells = 1L << 26;

    /// <summary>Tracks per axis of the resolved grid, and how many of them come before the explicit grid.</summary>
    public int Count0, Count1, Before0, Before1;

    /// <summary>True when auto-placement hit a limit and stacked items.</summary>
    public bool HitLimit;

    public void Place(Span<GridPlaceItem> items, in GridPlaceParams p, GridNameIndex names0, GridNameIndex names1)
    {
        HitLimit = false;
        _work = 0;
        int min0 = 0, min1 = 0, max0 = p.Explicit0, max1 = p.Explicit1;

        // Step 1: resolve the lines of every item; a definite axis gets an index in the explicit-grid line space (the first
        // explicit line is 0), an auto axis keeps its span.
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            ResolveAxis(it.ColStart, it.ColEnd, p.Explicit0, names0, out it.Def0, out it.Pos0, out it.Span0);
            ResolveAxis(it.RowStart, it.RowEnd, p.Explicit1, names1, out it.Def1, out it.Pos1, out it.Span1);
            it.Clamped = false;
            if (p.Sub0)
            {
                ClampSub(ref it.Def0, ref it.Pos0, ref it.Span0, p.Explicit0);
            }

            if (p.Sub1)
            {
                ClampSub(ref it.Def1, ref it.Pos1, ref it.Span1, p.Explicit1);
            }

            if (it.Def0)
            {
                min0 = Math.Min(min0, it.Pos0);
                max0 = Math.Max(max0, it.Pos0 + it.Span0);
            }

            if (it.Def1)
            {
                min1 = Math.Min(min1, it.Pos1);
                max1 = Math.Max(max1, it.Pos1 + it.Span1);
            }
        }

        Before0 = -min0;
        Before1 = -min1;
        int n0 = Math.Min(max0 - min0, GridTrackList.MaxTracks);
        int n1 = Math.Min(max1 - min1, GridTrackList.MaxTracks);
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            if (it.Def0)
            {
                it.Pos0 += Before0;
                Clamp(ref it.Pos0, ref it.Span0, n0, ref it.Clamped);
            }

            if (it.Def1)
            {
                it.Pos1 += Before1;
                Clamp(ref it.Pos1, ref it.Span1, n1, ref it.Clamped);
            }
        }

        // The auto-placement cursor works along the "minor" axis within the "major" one: rows are major for row flow.
        bool colFlow = p.ColumnFlow;
        int majorCount = colFlow ? n0 : n1;
        int minorCount = colFlow ? n1 : n0;

        // Anything auto in the minor axis may be wider than the grid: the implicit grid grows to the largest such span (Grid 2 §8.5 step 4).
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            bool defMinor = colFlow ? it.Def1 : it.Def0;
            int spanMinor = colFlow ? it.Span1 : it.Span0;
            if (!defMinor)
            {
                minorCount = Math.Max(minorCount, Math.Min(spanMinor, GridTrackList.MaxTracks));
            }
        }

        if ((colFlow ? p.Sub1 : p.Sub0))
        {
            minorCount = colFlow ? p.Explicit1 : p.Explicit0;
        }

        Reset(Math.Max(majorCount, 1), Math.Max(minorCount, 1));

        // Step 2: items with a definite position in both axes.
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            if (it.Def0 && it.Def1)
            {
                Mark(colFlow ? it.Pos0 : it.Pos1, colFlow ? it.Pos1 : it.Pos0, colFlow ? it.Span0 : it.Span1, colFlow ? it.Span1 : it.Span0);
            }
        }

        // Step 3: items locked to a major position (a definite row for row flow) find their minor position.
        int sparseMinor = 0;
        int lastMajor = -1;
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            bool defMajor = colFlow ? it.Def0 : it.Def1;
            bool defMinor = colFlow ? it.Def1 : it.Def0;
            if (!defMajor || defMinor)
            {
                continue;
            }

            int major = colFlow ? it.Pos0 : it.Pos1;
            int majorSpan = colFlow ? it.Span0 : it.Span1;
            int minorSpan = colFlow ? it.Span1 : it.Span0;
            if (major != lastMajor)
            {
                lastMajor = major;
                sparseMinor = 0;
            }

            int start = p.Dense ? 0 : sparseMinor;
            int minor = FindMinor(major, majorSpan, minorSpan, start, ref minorCount, colFlow ? p.Sub1 : p.Sub0);
            Mark(major, minor, majorSpan, minorSpan);
            sparseMinor = minor + minorSpan;
            Store(ref it, colFlow, minor, major);
        }

        // Step 4: the implicit grid's minor extent now includes everything with a definite minor position and the locked items above.
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            bool defMinor = colFlow ? it.Def1 : it.Def0;
            if (defMinor)
            {
                int pos = colFlow ? it.Pos1 : it.Pos0;
                int span = colFlow ? it.Span1 : it.Span0;
                minorCount = Math.Max(minorCount, Math.Min(pos + span, GridTrackList.MaxTracks));
            }
        }

        // Step 5: the rest, in order, with the cursor.
        int curMajor = 0, curMinor = 0;
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            bool defMajor = colFlow ? it.Def0 : it.Def1;
            bool defMinor = colFlow ? it.Def1 : it.Def0;
            if (defMajor)
            {
                continue;
            }

            int majorSpan = colFlow ? it.Span0 : it.Span1;
            int minorSpan = colFlow ? it.Span1 : it.Span0;
            majorSpan = Math.Min(majorSpan, GridTrackList.MaxTracks);
            minorSpan = Math.Min(minorSpan, GridTrackList.MaxTracks);
            if (_work > MaxWork || _rows >= GridTrackList.MaxTracks * 4)
            {
                // Hostile input: stack the rest below everything placed so far, one major track each.
                HitLimit = true;
                int stackedMajor = Math.Min(_rows, GridTrackList.MaxTracks - 1);
                int stackedMinor = defMinor ? (colFlow ? it.Pos1 : it.Pos0) : 0;
                Store(ref it, colFlow, stackedMinor, stackedMajor);
                majorCount = Math.Max(majorCount, stackedMajor + 1);
                continue;
            }

            if (defMinor)
            {
                int minor = colFlow ? it.Pos1 : it.Pos0;
                if (p.Dense)
                {
                    curMajor = 0;
                    curMinor = minor;
                }
                else
                {
                    if (minor < curMinor)
                    {
                        curMajor++;
                    }

                    curMinor = minor;
                }

                curMajor = FindMajor(curMajor, minor, majorSpan, minorSpan);
                Mark(curMajor, minor, majorSpan, minorSpan);
                Store(ref it, colFlow, minor, curMajor);
                majorCount = Math.Max(majorCount, curMajor + majorSpan);
                continue;
            }

            if (p.Dense)
            {
                curMajor = 0;
                curMinor = 0;
            }

            while (true)
            {
                if (_work++ > MaxWork)
                {
                    break;
                }

                if (curMinor + minorSpan <= minorCount && IsFree(curMajor, curMinor, majorSpan, minorSpan))
                {
                    break;
                }

                curMinor++;
                if (curMinor + minorSpan > minorCount)
                {
                    curMajor++;
                    curMinor = 0;
                    if (curMajor >= GridTrackList.MaxTracks * 4)
                    {
                        break;
                    }
                }
            }

            Mark(curMajor, curMinor, majorSpan, minorSpan);
            Store(ref it, colFlow, curMinor, curMajor);
            majorCount = Math.Max(majorCount, curMajor + majorSpan);
            curMinor += minorSpan;
        }

        majorCount = Math.Min(majorCount, GridTrackList.MaxTracks);
        minorCount = Math.Min(minorCount, GridTrackList.MaxTracks);
        Count0 = colFlow ? majorCount : minorCount;
        Count1 = colFlow ? minorCount : majorCount;
        Count0 = Math.Max(Count0, n0);
        Count1 = Math.Max(Count1, n1);
        if (p.Sub0)
        {
            Count0 = p.Explicit0;
        }

        if (p.Sub1)
        {
            Count1 = p.Explicit1;
        }

        // Final clamp of everything to the limit (Grid 2 §5.4).
        for (int i = 0; i < items.Length; i++)
        {
            ref GridPlaceItem it = ref items[i];
            Clamp(ref it.Pos0, ref it.Span0, Math.Max(Count0, 1), ref it.Clamped);
            Clamp(ref it.Pos1, ref it.Span1, Math.Max(Count1, 1), ref it.Clamped);
        }
    }

    private static void Store(ref GridPlaceItem it, bool colFlow, int minor, int major)
    {
        if (colFlow)
        {
            it.Pos0 = major;
            it.Pos1 = minor;
        }
        else
        {
            it.Pos1 = major;
            it.Pos0 = minor;
        }

        it.Def0 = it.Def1 = true;
    }

    private static void Clamp(ref int pos, ref int span, int count, ref bool clamped)
    {
        if (count <= 0)
        {
            count = 1;
        }

        if (pos >= count)
        {
            pos = count - 1;
            span = 1;
            clamped = true;
        }
        else if (pos + span > count)
        {
            span = count - pos;
            clamped = true;
        }

        if (span < 1)
        {
            span = 1;
        }
    }

    private static void ClampSub(ref bool def, ref int pos, ref int span, int count)
    {
        if (!def)
        {
            span = Math.Min(span, Math.Max(count, 1));
            return;
        }

        // A subgrid has no implicit tracks: the area is forced inside the explicit grid.
        if (count <= 0)
        {
            pos = 0;
            span = 1;
            return;
        }

        if (pos < 0)
        {
            span += pos;
            pos = 0;
            if (span < 1)
            {
                span = 1;
            }
        }

        if (pos >= count)
        {
            pos = count - 1;
            span = 1;
        }
        else if (pos + span > count)
        {
            span = count - pos;
        }
    }

    // ---- line resolution (Grid 2 §8.3, §8.3.1) ----

    /// <summary>Resolves a start and end line to a definite position (explicit-line index of the start, span) or, if auto, a span.</summary>
    internal static void ResolveAxis(GridLine start, GridLine end, int explicitCount, GridNameIndex names, out bool definite, out int pos, out int span)
    {
        definite = false;
        pos = 0;
        span = 1;
        bool sSpan = start.Kind == GridLineKind.Span, eSpan = end.Kind == GridLineKind.Span;
        bool sAuto = start.Kind == GridLineKind.Auto, eAuto = end.Kind == GridLineKind.Auto;

        // Two spans: the end one is dropped (§8.3.1).
        if (sSpan && eSpan)
        {
            end = GridLine.Auto;
            eSpan = false;
            eAuto = true;
        }

        if (sSpan && eAuto)
        {
            span = SpanCount(start);
            return;
        }

        if (eSpan && sAuto)
        {
            span = SpanCount(end);
            return;
        }

        if (sAuto && eAuto)
        {
            return;
        }

        int s = 0, e = 0;
        if (!sSpan && !sAuto)
        {
            s = LineIndex(start, explicitCount, names, false);
        }

        if (!eSpan && !eAuto)
        {
            e = LineIndex(end, explicitCount, names, true);
        }

        if (!sSpan && !sAuto && !eSpan && !eAuto)
        {
            if (s > e)
            {
                (s, e) = (e, s);
            }

            if (s == e)
            {
                e = s + 1;
            }
        }
        else if (!sSpan && !sAuto)
        {
            // start line, end auto or span
            e = eSpan ? EndForSpan(s, end, explicitCount, names) : s + 1;
        }
        else if (!eSpan && !eAuto)
        {
            s = sSpan ? StartForSpan(e, start, explicitCount, names) : e - 1;
        }

        definite = true;
        pos = s;
        span = Math.Max(1, e - s);
    }

    // Grid 2 §8.3.1: a span that counts a named line, with no definite line to count from, is a span of 1.
    private static int SpanCount(GridLine l) => l.Name is not null ? 1 : Math.Max(1, l.Number);

    internal static int LineIndex(GridLine l, int explicitCount, GridNameIndex names, bool isEnd)
    {
        int n = l.Number;
        string? name = l.Name;
        if (name is null)
        {
            int idx = n > 0 ? n - 1 : explicitCount + n + 1;
            return Math.Clamp(idx, -GridTrackList.MaxTracks, GridTrackList.MaxTracks);
        }

        if (n == 0)
        {
            // A bare identifier first names an area edge: foo-start / foo-end.
            int line = names.FirstWithSuffix(name, isEnd ? "-end" : "-start");
            if (line >= 0)
            {
                return line;
            }

            n = 1;
        }

        if (!names.TryGet(name, out List<int> lines))
        {
            return n > 0 ? Math.Min(explicitCount + n, GridTrackList.MaxTracks) : Math.Max(n, -GridTrackList.MaxTracks);
        }

        int k = lines.Count;
        if (n > 0)
        {
            if (n <= k)
            {
                return lines[n - 1];
            }

            return Math.Min(explicitCount + (n - k), GridTrackList.MaxTracks);
        }

        int m = -n;
        if (m <= k)
        {
            return lines[k - m];
        }

        return Math.Max(-(m - k), -GridTrackList.MaxTracks);
    }

    private static int EndForSpan(int start, GridLine span, int explicitCount, GridNameIndex names)
    {
        int n = Math.Max(1, span.Number);
        if (span.Name is null)
        {
            return start + n;
        }

        // The n-th line with that name after the start; past the explicit grid every line is assumed to have the name.
        if (names.TryGet(span.Name, out List<int> lines))
        {
            int found = 0;
            int last = start;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] > start)
                {
                    last = lines[i];
                    if (++found == n)
                    {
                        return last;
                    }
                }
            }

            return Math.Max(last, Math.Max(explicitCount, start)) + (n - found);
        }

        return Math.Max(explicitCount, start) + n;
    }

    private static int StartForSpan(int end, GridLine span, int explicitCount, GridNameIndex names)
    {
        _ = explicitCount;
        int n = Math.Max(1, span.Number);
        if (span.Name is null)
        {
            return end - n;
        }

        if (names.TryGet(span.Name, out List<int> lines))
        {
            int found = 0;
            int first = end;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i] < end)
                {
                    first = lines[i];
                    if (++found == n)
                    {
                        return first;
                    }
                }
            }

            return Math.Min(first, Math.Min(0, end)) - (n - found);
        }

        return Math.Min(0, end) - n;
    }

    // ---- occupancy ----

    private void Reset(int rows, int cols)
    {
        _rows = rows;
        _cols = cols;
        _words = (cols + 63) >> 6;
        long need = (long)_words * Math.Max(rows, 1);
        if (need > _bits.Length)
        {
            _bits = new ulong[Math.Max(need, _bits.Length * 2L)];
        }

        Array.Clear(_bits, 0, (int)need);
    }

    private void EnsureRows(int rows)
    {
        if (rows <= _rows)
        {
            return;
        }

        long need = (long)_words * rows;
        if (need > _bits.Length)
        {
            long size = Math.Max(need, _bits.Length * 2L);
            var nb = new ulong[Math.Min(size, Array.MaxLength)];
            Array.Copy(_bits, nb, (long)_words * _rows);
            _bits = nb;
        }
        else
        {
            Array.Clear(_bits, _words * _rows, (int)(need - (long)_words * _rows));
        }

        _rows = rows;
    }

    private void EnsureCols(int cols)
    {
        if (cols <= _cols)
        {
            return;
        }

        int words = (cols + 63) >> 6;
        if (words != _words)
        {
            long need = (long)words * _rows;
            var nb = new ulong[Math.Max(need, 64)];
            for (int r = 0; r < _rows; r++)
            {
                Array.Copy(_bits, (long)r * _words, nb, (long)r * words, _words);
            }

            _bits = nb;
            _words = words;
        }

        _cols = cols;
    }

    private bool IsFree(int major, int minor, int majorSpan, int minorSpan)
    {
        for (int r = major; r < major + majorSpan; r++)
        {
            if (r >= _rows)
            {
                break;
            }

            for (int c = minor; c < minor + minorSpan; c++)
            {
                _work++;
                if (c >= _cols)
                {
                    break;
                }

                if ((_bits[r * _words + (c >> 6)] & (1UL << (c & 63))) != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void Mark(int major, int minor, int majorSpan, int minorSpan)
    {
        if ((long)(major + majorSpan) * (minor + minorSpan) > MaxCells)
        {
            HitLimit = true;
            return;
        }

        EnsureRows(major + majorSpan);
        EnsureCols(minor + minorSpan);
        for (int r = major; r < major + majorSpan; r++)
        {
            for (int c = minor; c < minor + minorSpan; c++)
            {
                _work++;
                _bits[r * _words + (c >> 6)] |= 1UL << (c & 63);
            }
        }
    }

    private int FindMinor(int major, int majorSpan, int minorSpan, int from, ref int minorCount, bool bounded)
    {
        int minor = from;
        while (_work++ < MaxWork)
        {
            if ((!bounded || minor + minorSpan <= minorCount) && IsFree(major, minor, majorSpan, minorSpan))
            {
                break;
            }

            minor++;
            if (minor > GridTrackList.MaxTracks)
            {
                HitLimit = true;
                break;
            }

            if (bounded && minor + minorSpan > minorCount)
            {
                minor = Math.Max(0, minorCount - minorSpan);
                break;
            }
        }

        minorCount = Math.Max(minorCount, Math.Min(minor + minorSpan, GridTrackList.MaxTracks));
        return minor;
    }

    private int FindMajor(int from, int minor, int majorSpan, int minorSpan)
    {
        int major = from;
        while (_work++ < MaxWork && major < GridTrackList.MaxTracks * 4)
        {
            if (IsFree(major, minor, majorSpan, minorSpan))
            {
                break;
            }

            major++;
        }

        return major;
    }
}
