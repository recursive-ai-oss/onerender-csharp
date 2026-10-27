using OneRender.Style;

namespace OneRender.Tree;

public sealed partial class BoxTree
{
    // ---------------------------------------------------------------------------------------------------------------------
    // Structural edits: nodes inserted, removed, moved, or a display change that creates or destroys boxes.
    //
    // The boxes of a block container (the nearest ancestor of the edit whose box is a Container) are generated again from its styled
    // children, and nothing outside that container is touched. Its direct children whose nodes did not change keep their boxes, with
    // everything below them, so layout reuse and the display-list patch see them as the same boxes they were. Everything that is
    // decided by the siblings of a child (anonymous block wrapping, white-space trimming, table fix-up, blockification, the
    // continuations of inline boxes split around blocks) is decided by the same code a full build runs, over the container's children:
    // the anonymous boxes of the container are dissolved first and made again afterwards, so the result is the one a build from scratch
    // gives, apart from the numbers of the boxes.
    //
    // A box that is not kept goes back to a free list, and its slot is reused by the next box that is made: a UI that adds and removes
    // nodes every frame keeps the same columns forever.
    // ---------------------------------------------------------------------------------------------------------------------

    private int _regenBox = -1;
    private int _oldEpoch;
    private int _markStamp;
    private int[] _mark = [];
    private int[] _tgtMark = [];
    private int[] _tgt = new int[16];
    private int[] _tgtKey = new int[16];
    private int _tgtCount;
    private int[] _old = new int[64];
    private int _oldCount;
    private int[] _ds = new int[64];
    private int[] _rel = new int[64];
    private int[] _created = new int[64];
    private int _createdCount;

    /// <summary>The number of boxes reachable from the root; for tests, which compare it with <see cref="LiveBoxes"/>.</summary>
    internal int ReachableCount()
    {
        if (_count == 0)
        {
            return 0;
        }

        EnsureStack(_count);
        int sp = 0, n = 0;
        _stack[sp++] = 0;
        while (sp > 0)
        {
            int b = _stack[--sp];
            n++;
            for (int c = _first[b]; c >= 0; c = _next[c])
            {
                _stack[sp++] = c;
            }
        }

        return n;
    }

    /// <summary>The number of boxes in use: slots handed out minus slots on the free list.</summary>
    internal int LiveBoxes => _count - _freeBoxCount;

    private void EnsureNodeTables(StyledTree st)
    {
        int need = st.Count;
        if (_nodeBox.Length < need)
        {
            int old = _nodeBox.Length;
            int size = Math.Max(need, old * 2);
            Array.Resize(ref _nodeBox, size);
            Array.Fill(_nodeBox, -1, old, size - old);
        }

        if (_mark.Length < _nodeBox.Length)
        {
            _mark = new int[_nodeBox.Length];
            _tgtMark = new int[_nodeBox.Length];
            _markStamp = 0;
        }
    }

    // ---- freeing ----

    private void Discard(int c)
    {
        Unlink(c);
        ReleaseSubtree(c);
    }

    private void ReleaseSubtree(int root)
    {
        if (_first[root] < 0)
        {
            ReleaseOne(root);
            return;
        }

        int sp = 0;
        Ensure(ref _rel, 1);
        _rel[sp++] = root;
        while (sp > 0)
        {
            int b = _rel[--sp];
            for (int c = _first[b]; c >= 0; c = _next[c])
            {
                Ensure(ref _rel, sp + 1);
                _rel[sp++] = c;
            }

            ReleaseOne(b);
        }
    }

    private void ReleaseOne(int b)
    {
        int node = _node[b];
        if (node >= 0 && node < _nodeBox.Length && _nodeBox[node] == b)
        {
            _nodeBox[node] = -1;
        }

        _fresh[b] = 0;
        _parent[b] = -1;
        _first[b] = -1;
        _last[b] = -1;
        _next[b] = -1;
        _prev[b] = -1;
        _nextPiece[b] = -1;
        _node[b] = -1;
        if (_freeBoxCount == _freeBox.Length)
        {
            Array.Resize(ref _freeBox, _freeBoxCount * 2);
        }

        _freeBox[_freeBoxCount++] = b;
    }

    private static void Ensure(ref int[] array, int size)
    {
        if (array.Length < size)
        {
            Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }
    }

    // ---- keeping boxes ----

    private void LinkLast(int parent, int i)
    {
        _parent[i] = parent;
        _next[i] = -1;
        _prev[i] = _last[parent];
        if (_last[parent] >= 0)
        {
            _next[_last[parent]] = i;
        }
        else
        {
            _first[parent] = i;
        }

        _last[parent] = i;
    }

    // A direct child of the container being generated again keeps its box when it was one of the container's boxes before, its node did
    // not change, and it is a whole box (not a piece of an inline box split around a block).
    private bool TryKeep(int s, int parentBox)
    {
        int ob = _nodeBox[s];
        if (ob < 0 || _oldStamp[ob] != _oldEpoch || _mark[s] == _markStamp || _node[ob] != s
            || _nextPiece[ob] >= 0 || (_flags[ob] & (ushort)BoxTraits.Continuation) != 0)
        {
            return false;
        }

        _oldStamp[ob] = 0;
        LinkLast(parentBox, ob);
        if (_level[ob] == (byte)BoxLevel.Block && (_flags[ob] & (ushort)BoxTraits.OutOfFlow) == 0)
        {
            // The same rule as when a box is made (see Visit).
            _flags[parentBox] |= (ushort)BoxTraits.HasBlockChild;
        }

        return true;
    }

    // ---- finding the container to generate again ----

    // The styled node whose box generates again for a change at or below node a, or -1 when the change reaches no box. Inline boxes and
    // generated-content boxes on the way are marked: they are made again with everything below them.
    private int FindTarget(int a)
    {
        StyledTree st = Styled;
        while (a >= 0)
        {
            int b = _nodeBox[a];
            if (b >= 0 && _node[b] == a)
            {
                if ((BoxKind)_kind[b] == BoxKind.Container && st.KindOf(a) == (byte)StyledNodeKind.Element)
                {
                    return a;
                }

                if ((BoxKind)_kind[b] is BoxKind.Container or BoxKind.Inline)
                {
                    _mark[a] = _markStamp;
                    a = st.ParentOf(a);
                    continue;
                }

                return -1; // text, replaced content, a break or a marker has no boxes below it
            }

            if (st.KindOf(a) == (byte)StyledNodeKind.Element && st.Styles[st.StyleOf(a)].Display.IsContents
                && st.ReplacedOf(a) == ReplacedKind.None && !st.TagNameOf(a).Equals("br", StringComparison.Ordinal))
            {
                a = st.ParentOf(a); // its children are generated as its parent's (CSS Display 3 §2.5)
                continue;
            }

            return -1; // display: none, or inside something that is
        }

        return -1;
    }

    private void AddTarget(int node, StyledTree st)
    {
        if (node < 0 || _tgtMark[node] == _markStamp)
        {
            return;
        }

        _tgtMark[node] = _markStamp;
        if (_tgtCount == _tgt.Length)
        {
            Array.Resize(ref _tgt, _tgtCount * 2);
            Array.Resize(ref _tgtKey, _tgtCount * 2);
        }

        _tgt[_tgtCount] = node;
        _tgtKey[_tgtCount] = st.DepthOf(node);
        _tgtCount++;
    }

    /// <summary>
    /// Applies the structural part of the pending changes. Returns false when the whole tree has to be generated again instead (the
    /// root itself changed what it generates, or a limit was hit).
    /// </summary>
    private bool Restructure(CancellationToken cancellationToken)
    {
        StyledTree st = Styled;
        if (++_markStamp == int.MaxValue)
        {
            Array.Clear(_mark);
            Array.Clear(_tgtMark);
            _markStamp = 1;
        }

        int n = st.PendingCount;
        _tgtCount = 0;
        // Which nodes are made again, and which containers hold them.
        for (int k = 0; k < n; k++)
        {
            int node = st.PendingNode(k);
            if (IsDead(st, node))
            {
                continue;
            }

            NodeChanges kinds = st.PendingKinds(k);
            bool again = (kinds & (NodeChanges.Added | NodeChanges.TextStructure)) != 0;
            if ((kinds & NodeChanges.Style) != 0 && !SameStructure(in st.Styles[st.OldStyleOf(node)], in st.Styles[st.StyleOf(node)]))
            {
                again = true;
            }

            if (again)
            {
                if (node == 0)
                {
                    return false;
                }

                _mark[node] = _markStamp;
            }
        }

        for (int k = 0; k < n; k++)
        {
            int node = st.PendingNode(k);
            if (IsDead(st, node))
            {
                continue;
            }

            NodeChanges kinds = st.PendingKinds(k);
            if ((kinds & NodeChanges.Children) != 0 && _mark[node] != _markStamp)
            {
                AddTarget(FindTarget(node), st);
            }

            if (_mark[node] == _markStamp && node != 0)
            {
                AddTarget(FindTarget(st.ParentOf(node)), st);
            }
        }

        if (_tgtCount > 1)
        {
            // Outer containers first: a container made again by an outer one needs nothing more.
            Array.Sort(_tgtKey, _tgt, 0, _tgtCount);
        }

        EnsureStack(_count + 1);
        _structMode = true;
        _regenFailed = false;
        _createdCount = 0;
        bool ok = true;
        try
        {
            for (int t = 0; t < _tgtCount && ok; t++)
            {
                int x = _tgt[t];
                int c = _nodeBox[x];
                if (c < 0 || _fresh[c] != 0 || _node[c] != x || (BoxKind)_kind[c] != BoxKind.Container)
                {
                    continue;
                }

                Regenerate(x, c, cancellationToken);
                ok = !_regenFailed;
            }
        }
        finally
        {
            _structMode = false;
            _regenBox = -1;
            for (int i = 0; i < _createdCount; i++)
            {
                _fresh[_created[i]] = 0;
            }

            _createdCount = 0;
        }

        // Boxes were added, removed and renumbered under the painter's touched list.
        _touchedOverflow = true;
        return ok;
    }

    private static bool IsDead(StyledTree st, int node) => node != 0 && (st.IsFreeSlot(node) || st.ParentOf(node) < 0);

    private void Regenerate(int x, int c, CancellationToken cancellationToken)
    {
        StyledTree st = Styled;
        if (++_oldEpoch == int.MaxValue)
        {
            Array.Clear(_oldStamp);
            _oldEpoch = 1;
        }

        int createdStart = _createdCount;

        // Take the container's children out: anonymous boxes are dissolved (they are made again), the rest wait in _old.
        _oldCount = 0;
        int sp = 0;
        for (int ch = _last[c]; ch >= 0; ch = _prev[ch])
        {
            Ensure(ref _ds, sp + 1);
            _ds[sp++] = ch;
        }

        _first[c] = -1;
        _last[c] = -1;
        while (sp > 0)
        {
            int b = _ds[--sp];
            if ((_flags[b] & (ushort)BoxTraits.Anonymous) != 0 && _node[b] < 0)
            {
                for (int ch = _last[b]; ch >= 0; ch = _prev[ch])
                {
                    Ensure(ref _ds, sp + 1);
                    _ds[sp++] = ch;
                }

                ReleaseOne(b);
                continue;
            }

            Ensure(ref _old, _oldCount + 1);
            _old[_oldCount++] = b;
            _oldStamp[b] = _oldEpoch;
            _parent[b] = -1;
            _prev[b] = -1;
            _next[b] = -1;
        }

        _flags[c] &= unchecked((ushort)~BoxTraits.HasBlockChild);
        _childLayout[c] = (byte)ChildLayout.Empty;
        _regenBox = c;
        _top = -1;
        PushFrame(x, st.FirstChildOf(x), c, -1);
        RunFrames(cancellationToken);
        _regenBox = -1;

        // What was not kept is gone.
        for (int i = 0; i < _oldCount; i++)
        {
            int b = _old[i];
            if (_oldStamp[b] == _oldEpoch)
            {
                _oldStamp[b] = 0;
                ReleaseSubtree(b);
            }
        }

        SettleRegenerated(c, createdStart);
    }

    // The new boxes are dirty (they have no earlier layout), and so are the container and its ancestors; hazards follow the new children.
    private void SettleRegenerated(int c, int createdStart)
    {
        for (int i = createdStart; i < _createdCount; i++)
        {
            int b = _created[i];
            if (_fresh[b] == 1 && _dirty[b] == 0)
            {
                _dirty[b] = 1;
                if (_dirtyCount == _dirtyList.Length)
                {
                    Array.Resize(ref _dirtyList, _dirtyList.Length * 2);
                }

                _dirtyList[_dirtyCount++] = b;
            }
        }

        MarkDirty(c);

        // Hazards, children before parents, over the new boxes only; the kept ones carry theirs.
        EnsureStack(_count + 1);
        int sp = 0, n = 0;
        _stack[sp++] = c;
        while (sp > 0)
        {
            int b = _stack[--sp];
            _order[n++] = b;
            for (int ch = _first[b]; ch >= 0; ch = _next[ch])
            {
                if (_fresh[ch] != 0)
                {
                    _stack[sp++] = ch;
                }
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            int b = _order[i];
            byte h = OwnHazard(b);
            for (int ch = _first[b]; ch >= 0; ch = _next[ch])
            {
                h |= _hazard[ch];
            }

            _hazard[b] = h;
        }

        for (int b = _parent[c]; b >= 0; b = _parent[b])
        {
            byte h = OwnHazard(b);
            for (int ch = _first[b]; ch >= 0; ch = _next[ch])
            {
                h |= _hazard[ch];
            }

            if (_hazard[b] == h)
            {
                break;
            }

            _hazard[b] = h;
        }
    }
}
