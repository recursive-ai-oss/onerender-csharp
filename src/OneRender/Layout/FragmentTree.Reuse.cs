namespace OneRender.Layout;

public sealed partial class FragmentTree
{
    private int[] _copyStackDst = new int[64];
    private int[] _runMap = [];
    private int[] _runMapStamp = [];
    private int _runMapCall;

    /// <summary>
    /// Exchanges the contents of two trees of the same box tree. Layout writes into a scratch tree while it reads the previous result
    /// from the published one, then swaps, so the published object stays the same for everyone holding it.
    /// </summary>
    internal void SwapContents(FragmentTree other)
    {
        _version++;
        other._version++;
        (_kind, other._kind) = (other._kind, _kind);
        (_srcOf, other._srcOf) = (other._srcOf, _srcOf);
        (_copyShifted, other._copyShifted) = (other._copyShifted, _copyShifted);
        (_traits, other._traits) = (other._traits, _traits);
        (_box, other._box) = (other._box, _box);
        (_parent, other._parent) = (other._parent, _parent);
        (_first, other._first) = (other._first, _first);
        (_last, other._last) = (other._last, _last);
        (_next, other._next) = (other._next, _next);
        (_prev, other._prev) = (other._prev, _prev);
        (_nextOfBox, other._nextOfBox) = (other._nextOfBox, _nextOfBox);
        (_textStart, other._textStart) = (other._textStart, _textStart);
        (_textLength, other._textLength) = (other._textLength, _textLength);
        (_run, other._run) = (other._run, _run);
        (_fragmentainer, other._fragmentainer) = (other._fragmentainer, _fragmentainer);
        (_x, other._x) = (other._x, _x);
        (_y, other._y) = (other._y, _y);
        (_w, other._w) = (other._w, _w);
        (_h, other._h) = (other._h, _h);
        (_baseline, other._baseline) = (other._baseline, _baseline);
        (_border, other._border) = (other._border, _border);
        (_padding, other._padding) = (other._padding, _padding);
        (_overflow, other._overflow) = (other._overflow, _overflow);
        (_boxFirst, other._boxFirst) = (other._boxFirst, _boxFirst);
        (_boxLast, other._boxLast) = (other._boxLast, _boxLast);
        (_runs, other._runs) = (other._runs, _runs);
        (_runCount, other._runCount) = (other._runCount, _runCount);
        (_glyphId, other._glyphId) = (other._glyphId, _glyphId);
        (_glyphX, other._glyphX) = (other._glyphX, _glyphX);
        (_glyphY, other._glyphY) = (other._glyphY, _glyphY);
        (_glyphCluster, other._glyphCluster) = (other._glyphCluster, _glyphCluster);
        (_glyphCount, other._glyphCount) = (other._glyphCount, _glyphCount);
        (_count, other._count) = (other._count, _count);
        (_tbIndex, other._tbIndex) = (other._tbIndex, _tbIndex);
        (_tb, other._tb) = (other._tb, _tb);
        (_tbCount, other._tbCount) = (other._tbCount, _tbCount);
    }

    internal int BoxColumnOf(int fragment) => _box[fragment];

    internal int FirstFragmentOfBox(int box) => box < _boxFirst.Length ? _boxFirst[box] : -1;

    internal int CountInternal => _count;

    /// <summary>
    /// Appends copies of everything below <paramref name="sourceParent"/> in <paramref name="source"/> as children of
    /// <paramref name="parent"/> in this tree, in the same order and with the same rectangles, so a subtree laid out earlier is
    /// reused without laying it out again. Glyph runs are copied along; a run shared by several fragments stays shared.
    /// </summary>
    /// <remarks>
    /// Layout appends a subtree's fragments one after the other, so a subtree is normally one run of the columns and is copied
    /// column by column with the links moved by a constant. A subtree laid out in another order (an atomic inline moved into its
    /// line, say) is copied fragment by fragment.
    /// </remarks>
    internal void CopyDescendants(FragmentTree source, int sourceParent, int parent)
    {
        if (source._first[sourceParent] < 0)
        {
            return;
        }

        int n = _first[parent] < 0 ? ContiguousSubtree(source, sourceParent) : -1;
        if (n > 0)
        {
            CopyRange(source, sourceParent, n, parent);
        }
        else
        {
            CopyOneByOne(source, sourceParent, parent);
        }
    }

    // The number of fragments below sourceParent when they are exactly the indices after it in tree order, otherwise -1.
    private static int ContiguousSubtree(FragmentTree source, int sourceParent)
    {
        int n = 0;
        int cur = source._first[sourceParent];
        while (true)
        {
            if (cur != sourceParent + 1 + n)
            {
                return -1;
            }

            n++;
            int child = source._first[cur];
            if (child >= 0)
            {
                cur = child;
                continue;
            }

            int c = cur;
            while (true)
            {
                int next = source._next[c];
                if (next >= 0)
                {
                    cur = next;
                    break;
                }

                c = source._parent[c];
                if (c == sourceParent)
                {
                    return n;
                }

                if (c < 0)
                {
                    return -1;
                }
            }
        }
    }

    private void CopyRange(FragmentTree source, int sourceParent, int n, int parent)
    {
        int s0 = sourceParent + 1;
        int d0 = _count;
        int need = d0 + n;
        while (_kind.Length < need)
        {
            Grow();
        }

        int delta = d0 - s0;
        Array.Copy(source._kind, s0, _kind, d0, n);
        Array.Copy(source._traits, s0, _traits, d0, n);
        Array.Copy(source._box, s0, _box, d0, n);
        Array.Copy(source._textStart, s0, _textStart, d0, n);
        Array.Copy(source._textLength, s0, _textLength, d0, n);
        Array.Copy(source._fragmentainer, s0, _fragmentainer, d0, n);
        Array.Copy(source._x, s0, _x, d0, n);
        Array.Copy(source._y, s0, _y, d0, n);
        Array.Copy(source._w, s0, _w, d0, n);
        Array.Copy(source._h, s0, _h, d0, n);
        Array.Copy(source._baseline, s0, _baseline, d0, n);
        Array.Copy(source._border, s0, _border, d0, n);
        Array.Copy(source._padding, s0, _padding, d0, n);
        Array.Copy(source._overflow, s0, _overflow, d0, n);
        Shift(source._parent, s0, _parent, d0, n, delta);
        Shift(source._first, s0, _first, d0, n, delta);
        Shift(source._last, s0, _last, d0, n, delta);
        Shift(source._next, s0, _next, d0, n, delta);
        Shift(source._prev, s0, _prev, d0, n, delta);

        // The top-level fragments hang off the new parent.
        for (int c = d0; c >= 0; c = _next[c])
        {
            _parent[c] = parent;
        }

        _first[parent] = d0;
        _last[parent] = source._last[sourceParent] + delta;
        _count = need;
        _copyShifted |= delta != 0;

        CopyRuns(source, s0, d0, n);
        for (int k = 0; k < n; k++)
        {
            int i = d0 + k;
            _srcOf[i] = s0 + k;
            _nextOfBox[i] = -1;
            if ((FragmentKind)_kind[i] == FragmentKind.Box)
            {
                LinkBox(_box[i], i);
            }
        }
    }

    private void LinkBox(int box, int i)
    {
        if (_boxFirst.Length <= box)
        {
            int old = _boxFirst.Length;
            int size = Math.Max(Math.Max(Boxes.Count, box + 1), old * 2);
            Array.Resize(ref _boxFirst, size);
            Array.Resize(ref _boxLast, size);
            Array.Fill(_boxFirst, -1, old, size - old);
            Array.Fill(_boxLast, -1, old, size - old);
        }

        if (_boxLast[box] >= 0)
        {
            _nextOfBox[_boxLast[box]] = i;
        }
        else
        {
            _boxFirst[box] = i;
        }

        _boxLast[box] = i;
    }

    // Copies a column of fragment indices, moving every non-negative value by delta.
    private static void Shift(int[] source, int s0, int[] target, int d0, int n, int delta)
    {
        int k = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= System.Numerics.Vector<int>.Count)
        {
            var add = new System.Numerics.Vector<int>(delta);
            int width = System.Numerics.Vector<int>.Count;
            for (; k <= n - width; k += width)
            {
                var v = new System.Numerics.Vector<int>(source, s0 + k);
                var moved = System.Numerics.Vector.ConditionalSelect(System.Numerics.Vector.GreaterThanOrEqual(v, System.Numerics.Vector<int>.Zero), v + add, v);
                moved.CopyTo(target, d0 + k);
            }
        }

        for (; k < n; k++)
        {
            int v = source[s0 + k];
            target[d0 + k] = v >= 0 ? v + delta : v;
        }
    }

    // Gives the copied fragments glyph runs of their own. Runs that layout wrote one after the other (the usual case) move as a block.
    private void CopyRuns(FragmentTree source, int s0, int d0, int n)
    {
        int first = -1, last = -1;
        bool block = true;
        for (int k = 0; k < n; k++)
        {
            int r = source._run[s0 + k];
            if (r < 0)
            {
                continue;
            }

            if (first < 0)
            {
                first = r;
                last = r;
            }
            else if (r == last + 1)
            {
                last = r;
            }
            else if (r != last)
            {
                block = false;
                break;
            }
        }

        if (first < 0)
        {
            Array.Fill(_run, -1, d0, n);
            return;
        }

        if (block)
        {
            // The runs first..last must use back-to-back glyph ranges for their glyphs to move together.
            int glyphStart = source._runs[first].GlyphStart;
            int glyphEnd = glyphStart;
            for (int r = first; r <= last && block; r++)
            {
                block = source._runs[r].GlyphStart == glyphEnd;
                glyphEnd += source._runs[r].GlyphCount;
            }

            if (block)
            {
                int runBase = _runCount;
                int runs = last - first + 1;
                int glyphs = glyphEnd - glyphStart;
                while (_runs.Length < _runCount + runs)
                {
                    Array.Resize(ref _runs, _runs.Length * 2);
                }

                if (_glyphCount + glyphs > _glyphId.Length)
                {
                    int size = (int)Math.Min(Array.MaxLength, Math.Max((long)_glyphId.Length * 2, (long)_glyphCount + glyphs));
                    Array.Resize(ref _glyphId, size);
                    Array.Resize(ref _glyphX, size);
                    Array.Resize(ref _glyphY, size);
                    Array.Resize(ref _glyphCluster, size);
                }

                Array.Copy(source._glyphId, glyphStart, _glyphId, _glyphCount, glyphs);
                Array.Copy(source._glyphX, glyphStart, _glyphX, _glyphCount, glyphs);
                Array.Copy(source._glyphY, glyphStart, _glyphY, _glyphCount, glyphs);
                Array.Copy(source._glyphCluster, glyphStart, _glyphCluster, _glyphCount, glyphs);
                int shift = _glyphCount - glyphStart;
                for (int r = 0; r < runs; r++)
                {
                    GlyphRun g = source._runs[first + r];
                    _runs[_runCount + r] = new GlyphRun(g.Face, g.FontSize, g.GlyphStart + shift, g.GlyphCount, g.TextStart, g.TextLength, g.RightToLeft);
                }

                _runCount += runs;
                _glyphCount += glyphs;
                int delta = runBase - first;
                for (int k = 0; k < n; k++)
                {
                    int r = source._run[s0 + k];
                    _run[d0 + k] = r < 0 ? -1 : r + delta;
                }

                return;
            }
        }

        CopyRunsOneByOne(source, s0, d0, n);
    }

    private void CopyRunsOneByOne(FragmentTree source, int s0, int d0, int n)
    {
        _runMapCall++;
        if (_runMap.Length < source._runCount)
        {
            int size = Math.Max(source._runCount, _runMap.Length * 2);
            _runMap = new int[size];
            _runMapStamp = new int[size];
        }

        for (int k = 0; k < n; k++)
        {
            int run = source._run[s0 + k];
            if (run < 0)
            {
                _run[d0 + k] = -1;
                continue;
            }

            if (_runMapStamp[run] != _runMapCall)
            {
                GlyphRun r = source._runs[run];
                GlyphRunId id = AddGlyphRun(
                    r.Face, r.FontSize, r.TextStart, r.TextLength, r.RightToLeft,
                    new ReadOnlySpan<int>(source._glyphId, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<double>(source._glyphX, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<double>(source._glyphY, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<int>(source._glyphCluster, r.GlyphStart, r.GlyphCount));
                _runMap[run] = id.Index;
                _runMapStamp[run] = _runMapCall;
            }

            _run[d0 + k] = _runMap[run];
        }
    }

    private void CopyOneByOne(FragmentTree source, int sourceParent, int parent)
    {
        _runMapCall++;
        if (_runMap.Length < source._runCount)
        {
            int size = Math.Max(source._runCount, _runMap.Length * 2);
            _runMap = new int[size];
            _runMapStamp = new int[size];
        }

        // Pre-order, as layout appends: a box's fragments keep their order along the box chain.
        int depth = 0;
        _copyStackDst[0] = parent;
        int s = sourceParent;
        int cur = source._first[s];
        while (true)
        {
            if (cur >= 0)
            {
                int n = CopyOne(source, cur, _copyStackDst[depth]);
                if (source._first[cur] >= 0)
                {
                    depth++;
                    if (depth == _copyStackDst.Length)
                    {
                        Array.Resize(ref _copyStackDst, depth * 2);
                    }

                    _copyStackDst[depth] = n;
                    s = cur;
                    cur = source._first[cur];
                }
                else
                {
                    cur = source._next[cur];
                }
            }
            else
            {
                if (depth == 0)
                {
                    break;
                }

                cur = source._next[s];
                s = source._parent[s];
                depth--;
            }
        }
    }

    private int CopyOne(FragmentTree source, int c, int parent)
    {
        var kind = (FragmentKind)source._kind[c];
        var rect = new Rect(source._x[c], source._y[c], source._w[c], source._h[c]);
        int i = Append(kind, new Fragment(this, parent), source._box[c], rect);
        _srcOf[i] = c;
        _copyShifted |= i != c;
        _traits[i] = source._traits[c];
        _textStart[i] = source._textStart[c];
        _textLength[i] = source._textLength[c];
        _fragmentainer[i] = source._fragmentainer[c];
        _baseline[i] = source._baseline[c];
        _border[i] = source._border[c];
        _padding[i] = source._padding[c];
        _overflow[i] = source._overflow[c];
        int run = source._run[c];
        if (run >= 0)
        {
            if (_runMapStamp[run] != _runMapCall)
            {
                GlyphRun r = source._runs[run];
                GlyphRunId id = AddGlyphRun(
                    r.Face, r.FontSize, r.TextStart, r.TextLength, r.RightToLeft,
                    new ReadOnlySpan<int>(source._glyphId, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<double>(source._glyphX, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<double>(source._glyphY, r.GlyphStart, r.GlyphCount),
                    new ReadOnlySpan<int>(source._glyphCluster, r.GlyphStart, r.GlyphCount));
                _runMap[run] = id.Index;
                _runMapStamp[run] = _runMapCall;
            }

            _run[i] = _runMap[run];
        }

        if (kind == FragmentKind.Box)
        {
            int box = source._box[c];
            if (_boxFirst.Length <= box)
            {
                int old = _boxFirst.Length;
                int size = Math.Max(Math.Max(Boxes.Count, box + 1), old * 2);
                Array.Resize(ref _boxFirst, size);
                Array.Resize(ref _boxLast, size);
                Array.Fill(_boxFirst, -1, old, size - old);
                Array.Fill(_boxLast, -1, old, size - old);
            }

            if (_boxLast[box] >= 0)
            {
                _nextOfBox[_boxLast[box]] = i;
            }
            else
            {
                _boxFirst[box] = i;
            }

            _boxLast[box] = i;
        }

        return i;
    }
}
