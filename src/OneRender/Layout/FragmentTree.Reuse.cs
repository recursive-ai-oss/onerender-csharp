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
    internal void CopyDescendants<T>(FragmentTree source, int sourceParent, int parent, ref T observer)
        where T : struct, IFragmentCopyObserver
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
                if ((FragmentKind)source._kind[cur] == FragmentKind.Box)
                {
                    observer.Copied(source._box[cur], cur, n);
                }

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

/// <summary>Told about each box fragment copied by <see cref="FragmentTree.CopyDescendants{T}"/>.</summary>
internal interface IFragmentCopyObserver
{
    /// <summary>A fragment of <paramref name="box"/> was copied from <paramref name="sourceFragment"/> to <paramref name="newFragment"/>.</summary>
    void Copied(int box, int sourceFragment, int newFragment);
}
