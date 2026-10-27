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
        int first = _first[parent];
        _first[parent] = -1;
        _last[parent] = -1;
        return first;
    }

    /// <summary>Appends an existing fragment (detached, or a fresh piece) as the last child of <paramref name="parent"/>.</summary>
    internal void AppendChild(int parent, int child)
    {
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

    internal void SetFragmentainer(int i, int index) => _fragmentainer[i] = index;

    internal void SetRaw(int i, Rect rect, Insets border, Insets padding)
    {
        _x[i] = rect.X;
        _y[i] = rect.Y;
        _w[i] = rect.Width;
        _h[i] = rect.Height;
        _border[i] = border;
        _padding[i] = padding;
    }

    internal void SetOverflowRaw(int i, Rect overflow) => _overflow[i] = overflow;

    internal void SetKindFlag(int i, FragmentTraits traits) => _traits[i] = (byte)traits;
}
