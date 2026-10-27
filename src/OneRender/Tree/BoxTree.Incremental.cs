using OneRender.Style;

namespace OneRender.Tree;

/// <summary>What <see cref="BoxTree.ApplyChanges"/> did.</summary>
internal enum BoxUpdate : byte
{
    /// <summary>Nothing in the change set reached the boxes.</summary>
    None,

    /// <summary>Styles and text were patched into existing boxes; the box structure is unchanged and the touched boxes are marked dirty.</summary>
    Patched,

    /// <summary>Only values that paint reads changed (colors, shadows, opacity, transforms ...): the boxes were patched, nothing is dirty for layout and the display list needs updating.</summary>
    PatchedPaintOnly,

    /// <summary>The boxes were generated again, so box indices and everything keyed by them are new.</summary>
    Rebuilt,
}

public sealed partial class BoxTree
{
    // What makes a subtree not reusable from an earlier layout: its layout leaks into or reads state outside the subtree
    // (floats, out-of-flow positioning, columns) or keeps side tables keyed by fragment (tables).
    internal const byte HazardOutOfFlow = 1;
    internal const byte HazardColumns = 2;
    internal const byte HazardTable = 4;

    private byte[] _dirty = new byte[64];
    private byte[] _hazard = new byte[64];
    private int[] _dirtyList = new int[64];
    private int _dirtyCount;
    private int _epoch;

    /// <summary>Counts the full builds; layout results kept from an earlier epoch cannot be reused.</summary>
    internal int Epoch => _epoch;

    internal int DirtyCount => _dirtyCount;

    internal int DirtyAt(int i) => _dirtyList[i];

    internal bool IsDirty(int box) => _dirty[box] != 0;

    internal byte HazardOf(int box) => _hazard[box];

    /// <summary>Forgets the dirty marks once layout has used them.</summary>
    internal void ClearDirty()
    {
        for (int i = 0; i < _dirtyCount; i++)
        {
            _dirty[_dirtyList[i]] = 0;
        }

        _dirtyCount = 0;
    }

    /// <summary>Marks a box and its ancestors as having a changed subtree.</summary>
    internal void MarkDirty(int box)
    {
        for (int b = box; b >= 0 && _dirty[b] == 0; b = _parent[b])
        {
            _dirty[b] = 1;
            if (_dirtyCount == _dirtyList.Length)
            {
                Array.Resize(ref _dirtyList, _dirtyList.Length * 2);
            }

            _dirtyList[_dirtyCount++] = b;
        }
    }

    private void PrepareIncremental()
    {
        _epoch++;
        if (_dirty.Length < _count)
        {
            int size = Math.Max(_count, _dirty.Length * 2);
            _dirty = new byte[size];
            _hazard = new byte[size];
        }

        Array.Clear(_dirty, 0, _count);
        _dirtyCount = 0;
        ComputeHazards();
    }

    // Bottom-up over the finished tree with an explicit stack.
    private void ComputeHazards()
    {
        if (_count == 0)
        {
            return;
        }

        EnsureStack(_count);
        int sp = 0;
        _stack[sp++] = 0;
        // Pre-order push, then settle in reverse: every child comes after its parent in this order, so a reverse sweep sees children first.
        int n = 1;
        _order[0] = 0;
        while (sp > 0)
        {
            int b = _stack[--sp];
            for (int c = _first[b]; c >= 0; c = _next[c])
            {
                _order[n++] = c;
                _stack[sp++] = c;
            }
        }

        for (int i = n - 1; i >= 0; i--)
        {
            int b = _order[i];
            byte h = OwnHazard(b);
            for (int c = _first[b]; c >= 0; c = _next[c])
            {
                h |= _hazard[c];
            }

            _hazard[b] = h;
        }
    }

    private int[] _stack = new int[64];
    private int[] _order = new int[64];

    private void EnsureStack(int size)
    {
        if (_stack.Length < size)
        {
            _stack = new int[Math.Max(size, _stack.Length * 2)];
            _order = new int[_stack.Length];
        }
    }

    private byte OwnHazard(int b)
    {
        BoxKind kind = (BoxKind)_kind[b];
        if (kind is not (BoxKind.Container or BoxKind.Replaced or BoxKind.Inline))
        {
            return 0;
        }

        ref readonly ComputedValues v = ref Styled.Styles[new StyleId(_style[b])];
        byte h = 0;
        if (v.Floating != FloatKind.None || v.Position is PositionKind.Absolute or PositionKind.Fixed)
        {
            h |= HazardOutOfFlow;
        }

        if (v.IsMulticol)
        {
            h |= HazardColumns;
        }

        if ((FormattingContext)_ctx[b] >= FormattingContext.Table)
        {
            h |= HazardTable;
        }

        return h;
    }

    // The part of a style that decides which boxes exist and how they are wrapped and flagged (BoxTree.Build reads exactly these).
    private static bool SameStructure(in ComputedValues a, in ComputedValues b) =>
        a.Display == b.Display
        && a.IsOutOfFlow == b.IsOutOfFlow
        && a.WhiteSpaceCollapse == b.WhiteSpaceCollapse
        && a.ListStylePosition == b.ListStylePosition;

    /// <summary>
    /// Brings the boxes up to date with the styled tree's pending changes. A change that cannot alter which boxes exist is patched
    /// into place and the affected boxes and their ancestors are marked dirty; any other change generates the boxes again.
    /// </summary>
    internal BoxUpdate ApplyChanges(CancellationToken cancellationToken = default)
    {
        StyledTree st = Styled;
        if (_count == 0 || st.PendingOverflowed || _nodeBox.Length < st.Count)
        {
            Build(cancellationToken);
            return BoxUpdate.Rebuilt;
        }

        int n = st.PendingCount;
        // First decide whether the structure is touched; patching must not start on a tree that is about to be rebuilt.
        for (int k = 0; k < n; k++)
        {
            NodeChanges kinds = st.PendingKinds(k);
            if ((kinds & (NodeChanges.TextStructure | NodeChanges.Children | NodeChanges.Added | NodeChanges.Removed)) != 0)
            {
                Build(cancellationToken);
                return BoxUpdate.Rebuilt;
            }

            if ((kinds & NodeChanges.Style) != 0)
            {
                int node = st.PendingNode(k);
                if (!SameStructure(in st.Styles[st.OldStyleOf(node)], in st.Styles[st.StyleOf(node)]))
                {
                    Build(cancellationToken);
                    return BoxUpdate.Rebuilt;
                }
            }
        }

        bool layoutAny = false, paintAny = false;
        for (int k = 0; k < n; k++)
        {
            NodeChanges kinds = st.PendingKinds(k);
            int node = st.PendingNode(k);
            int principal = _nodeBox[node];
            if (principal < 0)
            {
                continue;
            }

            if ((kinds & NodeChanges.Style) != 0)
            {
                StyleId sid = st.StyleOf(node);
                bool layout = !ComputedValues.DiffersOnlyInPaint(in st.Styles[st.OldStyleOf(node)], in st.Styles[sid]);
                for (int b = principal; b >= 0; b = _nextPiece[b])
                {
                    PatchStyle(b, sid, layout);
                }

                layoutAny |= layout;
                paintAny = true;
            }

            if ((kinds & (NodeChanges.Text | NodeChanges.Replaced)) != 0)
            {
                for (int b = principal; b >= 0; b = _nextPiece[b])
                {
                    MarkDirty(b);
                }

                layoutAny = true;
            }
        }

        return layoutAny ? BoxUpdate.Patched : paintAny ? BoxUpdate.PatchedPaintOnly : BoxUpdate.None;
    }

    /// <summary>
    /// Brings the boxes up to date after style rows were rewritten in place (an animation tick): the boxes of the given nodes keep their
    /// style ids, whose content changed. Anonymous boxes below them take their parents' values again, hazards are recomputed, and with
    /// <paramref name="layout"/> the boxes are marked for layout.
    /// </summary>
    /// <param name="nodes">Styled node indices whose rows changed.</param>
    /// <param name="layout">True when a property that affects layout changed.</param>
    internal void ApplyAnimatedRows(ReadOnlySpan<int> nodes, bool layout)
    {
        foreach (int node in nodes)
        {
            if ((uint)node >= (uint)_nodeBox.Length)
            {
                continue;
            }

            for (int b = _nodeBox[node]; b >= 0; b = _nextPiece[b])
            {
                PatchStyle(b, new StyleId(_style[b]), layout);
            }
        }
    }

    private void PatchStyle(int box, StyleId sid, bool layout)
    {
        if (_style[box] != sid.Index)
        {
            _style[box] = sid.Index;
            byte own = OwnHazard(box);
            UpdateHazard(box, own);
        }

        if (layout)
        {
            MarkDirty(box);
        }

        RefreshAnonymousBelow(box, layout);
    }

    private void UpdateHazard(int box, byte own)
    {
        for (int b = box; b >= 0; b = _parent[b])
        {
            byte h = b == box ? own : OwnHazard(b);
            for (int c = _first[b]; c >= 0; c = _next[c])
            {
                h |= _hazard[c];
            }

            if (_hazard[b] == h)
            {
                return;
            }

            _hazard[b] = h;
        }
    }

    // Anonymous boxes take their style from their parent box; when the parent's style changes theirs follow.
    private void RefreshAnonymousBelow(int root, bool layout)
    {
        EnsureStack(_count);
        int sp = 0;
        _stack[sp++] = root;
        while (sp > 0)
        {
            int p = _stack[--sp];
            for (int c = _first[p]; c >= 0; c = _next[c])
            {
                if (_node[c] >= 0)
                {
                    continue;
                }

                DisplayType display = Styled.Styles[new StyleId(_style[c])].Display;
                StyleId ns = Styled.Styles.GetAnonymous(new StyleId(_style[p]), display);
                if (ns.Index != _style[c])
                {
                    _style[c] = ns.Index;
                    if (layout)
                    {
                        MarkDirty(c);
                    }
                }

                _stack[sp++] = c;
            }
        }
    }
}
