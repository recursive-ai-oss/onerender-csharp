namespace OneRender.Layout;

// Fragmentation rewrites a laid-out subtree into fragmentainers: it moves fragments between parents and splits a box into one
// piece per fragmentainer. These operations stay internal; the fragment tree's public surface is read-only for them.
public sealed partial class FragmentTree
{
    /// <summary>Appends a fragmentainer fragment (a column) as the last child of <paramref name="parent"/>.</summary>
    internal int AddColumnFragment(int parent, Rect rect, int index)
    {
        int i = Append(FragmentKind.Column, new Fragment(this, parent), -1, rect);
        _fragmentainer[i] = index;
        return i;
    }

    /// <summary>Removes all children of <paramref name="parent"/> from its list and returns the first one (their sibling links stay, so the caller can walk them).</summary>
    internal int DetachChildren(int parent)
    {
        _copyShifted = true;
        int first = _first[parent];
        _first[parent] = -1;
        _last[parent] = -1;
        return first;
    }

    /// <summary>Appends an existing fragment (detached, or a fresh piece) as the last child of <paramref name="parent"/>.</summary>
    internal void AppendChild(int parent, int child)
    {
        _copyShifted = true;
        _parent[child] = parent;
        _next[child] = -1;
        if (_last[parent] >= 0)
        {
            _next[_last[parent]] = child;
        }
        else
        {
            _first[parent] = child;
        }

        _last[parent] = child;
    }

    /// <summary>
    /// Creates the next piece of a box fragment: a copy of <paramref name="source"/> without children, linked into the box's piece
    /// chain right after <paramref name="previous"/>. The new fragment is not yet attached to a parent.
    /// </summary>
    internal int NewPiece(int source, int previous, Rect rect)
    {
        _copyShifted = true;
        if (_count == _kind.Length)
        {
            Grow();
        }

        int i = _count++;
        _kind[i] = _kind[source];
        _traits[i] = _traits[source];
        _box[i] = _box[source];
        _parent[i] = -1;
        _first[i] = -1;
        _last[i] = -1;
        _next[i] = -1;
        _textStart[i] = _textStart[source];
        _textLength[i] = _textLength[source];
        _run[i] = _run[source];
        _fragmentainer[i] = 0;
        _x[i] = rect.X;
        _y[i] = rect.Y;
        _w[i] = rect.Width;
        _h[i] = rect.Height;
        _baseline[i] = _baseline[source];
        _border[i] = _border[source];
        _padding[i] = _padding[source];
        _overflow[i] = new Rect(0, 0, rect.Width, rect.Height);
        int b = _box[source];
        _nextOfBox[i] = _nextOfBox[previous];
        _nextOfBox[previous] = i;
        if (b >= 0 && _boxLast[b] == previous)
        {
            _boxLast[b] = i;
        }

        return i;
    }

    /// <summary>
    /// Copies the subtree of <paramref name="src"/> as the last child of <paramref name="parent"/>, with the root of the copy at
    /// <paramref name="rect"/>. Box fragments of the copy join their box's chain of pieces; every fragment of the copy is marked
    /// <see cref="FragmentTraits.Repeated"/>. Glyph runs are shared.
    /// </summary>
    internal int CloneSubtree(int src, int parent, Rect rect)
    {
        _copyShifted = true;
        int root = CopyOne(src);
        _x[root] = rect.X;
        _y[root] = rect.Y;
        _w[root] = rect.Width;
        _h[root] = rect.Height;
        AppendChild(parent, root);
        var stack = new Stack<(int Src, int Dst, int Next)>();
        stack.Push((src, root, _first[src]));
        while (stack.Count > 0)
        {
            (int s, int d, int next) = stack.Pop();
            if (next < 0)
            {
                continue;
            }

            stack.Push((s, d, _next[next]));
            int copy = CopyOne(next);
            AppendChild(d, copy);
            stack.Push((next, copy, _first[next]));
        }

        return root;
    }

    private int CopyOne(int source)
    {
        if (_count == _kind.Length)
        {
            Grow();
        }

        int i = _count++;
        _srcOf[i] = -1;
        _kind[i] = _kind[source];
        _traits[i] = (byte)(_traits[source] | (byte)FragmentTraits.Repeated);
        _box[i] = _box[source];
        _parent[i] = -1;
        _first[i] = -1;
        _last[i] = -1;
        _next[i] = -1;
        _prev[i] = -1;
        _nextOfBox[i] = -1;
        _textStart[i] = _textStart[source];
        _textLength[i] = _textLength[source];
        _run[i] = _run[source];
        _fragmentainer[i] = _fragmentainer[source];
        _x[i] = _x[source];
        _y[i] = _y[source];
        _w[i] = _w[source];
        _h[i] = _h[source];
        _baseline[i] = _baseline[source];
        _border[i] = _border[source];
        _padding[i] = _padding[source];
        _overflow[i] = _overflow[source];
        int b = _box[source];
        if ((FragmentKind)_kind[source] == FragmentKind.Box && b >= 0 && b < _boxLast.Length && _boxLast[b] >= 0)
        {
            _nextOfBox[_boxLast[b]] = i;
            _boxLast[b] = i;
        }

        return i;
    }

    internal void SetFragmentainer(int i, int index)
    {
        _copyShifted = true;
        _fragmentainer[i] = index;
    }

    internal void SetRaw(int i, Rect rect, Insets border, Insets padding)
    {
        _copyShifted = true;
        _x[i] = rect.X;
        _y[i] = rect.Y;
        _w[i] = rect.Width;
        _h[i] = rect.Height;
        _border[i] = border;
        _padding[i] = padding;
    }

    internal void SetOverflowRaw(int i, Rect overflow)
    {
        _copyShifted = true;
        _overflow[i] = overflow;
    }

    internal void SetKindFlag(int i, FragmentTraits traits)
    {
        _copyShifted = true;
        _traits[i] = (byte)traits;
    }
}
