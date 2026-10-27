using OneRender.Style;

namespace OneRender.Tree;

/// <summary>
/// The box tree (CSS Display 3 §2, CSS2 §9.2): the boxes generated from a <see cref="StyledTree"/>, in struct-of-arrays
/// columns. Block-level and inline-level boxes are siblings only where CSS allows it: a container's in-flow children are
/// all inline-level (it establishes an inline formatting context) or all block-level, with anonymous block boxes wrapping
/// runs of inline-level children, and inline boxes split around block-level descendants.
/// </summary>
/// <remarks>
/// <see cref="Build"/> regenerates every box. The columns, work stacks and style rows are kept between builds, so building the
/// same styled tree again allocates nothing. Not thread-safe.
/// </remarks>
public sealed partial class BoxTree
{
    private byte[] _kind;
    private byte[] _level;
    private byte[] _ctx;
    private byte[] _childLayout;
    private ushort[] _flags;
    private int[] _style;
    private int[] _node;
    private int[] _parent;
    private int[] _first;
    private int[] _last;
    private int[] _next;
    private int[] _prev;
    private int[] _nextPiece;
    private int[] _nodeBox = [];
    private int[] _oldStamp = new int[64];
    private byte[] _fresh = new byte[64];
    private int[] _freeBox = new int[64];
    private int _freeBoxCount;
    // True while boxes are being generated for an edit: new boxes then reuse freed slots and are recorded in _created.
    private bool _structMode;
    private bool _regenFailed;
    private int _count;
    private bool _limitReported;

    /// <summary>Creates an empty box tree over <paramref name="styled"/>.</summary>
    /// <param name="styled">The styled tree the boxes are generated from. Its style table also styles anonymous boxes.</param>
    public BoxTree(StyledTree styled)
    {
        ArgumentNullException.ThrowIfNull(styled);
        Styled = styled;
        _kind = new byte[64];
        _level = new byte[64];
        _ctx = new byte[64];
        _childLayout = new byte[64];
        _flags = new ushort[64];
        _style = new int[64];
        _node = new int[64];
        _parent = new int[64];
        _first = new int[64];
        _last = new int[64];
        _next = new int[64];
        _prev = new int[64];
        _nextPiece = new int[64];
    }

    /// <summary>The styled tree the boxes were generated from.</summary>
    public StyledTree Styled { get; }

    /// <summary>The number of boxes, including boxes that were removed again (collapsed white space) and are no longer reachable.</summary>
    public int Count => _count;

    /// <summary>The most boxes one build creates: twice the styled tree's node limit. Further boxes are not made and a diagnostic says so.</summary>
    public int MaxBoxes => (int)Math.Min(int.MaxValue / 2L, (long)Styled.Limits.MaxNodes * 2);

    /// <summary>The root box (the root element's), or the null handle when nothing was generated.</summary>
    public Box Root => _count == 0 ? default : new Box(this, 0);

    /// <summary>
    /// The principal box of a styled node: the box generated for the element itself, or for the first piece of a split inline
    /// box. The null handle for nodes that generate no box (<c>display: none</c>, <c>display: contents</c>, collapsed white space).
    /// </summary>
    /// <param name="node">A node of <see cref="Styled"/>.</param>
    public Box PrincipalBox(StyledNode node)
    {
        if (!node.IsValid || !ReferenceEquals(node.Tree, Styled))
        {
            return default;
        }

        return node.Index < _nodeBox.Length ? Make(_nodeBox[node.Index]) : default;
    }

    internal Box Make(int index) => index < 0 ? default : new Box(this, index);

    internal byte KindOf(int i) => _kind[i];

    internal byte LevelOf(int i) => _level[i];

    internal byte ContextOf(int i) => _ctx[i];

    internal byte ChildLayoutOf(int i) => _childLayout[i];

    internal ushort FlagsOf(int i) => _flags[i];

    internal StyleId StyleOf(int i) => new(_style[i]);

    internal StyledNode NodeOf(int i) => Styled.Make(_node[i]);

    internal int ParentOf(int i) => _parent[i];

    /// <summary>The styled node a box was made from, or -1 for an anonymous box.</summary>
    internal int NodeIndexOf(int i) => _node[i];

    internal int FirstChildOf(int i) => _first[i];

    internal int LastChildOf(int i) => _last[i];

    internal int NextSiblingOf(int i) => _next[i];

    internal int PreviousSiblingOf(int i) => _prev[i];

    internal int NextPieceOf(int i) => _nextPiece[i];

    /// <summary>Creates a box and appends it to <paramref name="parent"/>'s children (no parent for -1). Returns -1 at the box limit.</summary>
    private int NewBox(BoxKind kind, BoxLevel level, FormattingContext ctx, BoxTraits flags, StyleId style, int node, int parent)
    {
        int i;
        if (_structMode && _freeBoxCount > 0)
        {
            i = _freeBox[--_freeBoxCount];
        }
        else
        {
            if (_count >= MaxBoxes)
            {
                if (!_limitReported)
                {
                    _limitReported = true;
                    Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "box limit of " + MaxBoxes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " reached; later boxes are not generated");
                }

                _regenFailed = true;
                return -1;
            }

            if (_count == _kind.Length)
            {
                Grow();
            }

            i = _count++;
        }

        if (_structMode)
        {
            _fresh[i] = 1;
            if (_createdCount == _created.Length)
            {
                Array.Resize(ref _created, _createdCount * 2);
            }

            _created[_createdCount++] = i;
        }

        _oldStamp[i] = 0;
        _kind[i] = (byte)kind;
        _level[i] = (byte)level;
        _ctx[i] = (byte)ctx;
        _childLayout[i] = (byte)ChildLayout.Empty;
        _flags[i] = (ushort)flags;
        _style[i] = style.Index;
        _node[i] = node;
        _parent[i] = parent;
        _first[i] = -1;
        _last[i] = -1;
        _next[i] = -1;
        _prev[i] = -1;
        _nextPiece[i] = -1;
        if (parent >= 0)
        {
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

        return i;
    }

    /// <summary>Removes a box from its parent's child list. The box stays allocated but unreachable.</summary>
    private void Unlink(int i)
    {
        int p = _parent[i], prev = _prev[i], next = _next[i];
        if (prev >= 0)
        {
            _next[prev] = next;
        }
        else if (p >= 0)
        {
            _first[p] = next;
        }

        if (next >= 0)
        {
            _prev[next] = prev;
        }
        else if (p >= 0)
        {
            _last[p] = prev;
        }

        _parent[i] = -1;
        _prev[i] = -1;
        _next[i] = -1;
    }

    private void Grow()
    {
        int n = _kind.Length * 2;
        Array.Resize(ref _kind, n);
        Array.Resize(ref _level, n);
        Array.Resize(ref _ctx, n);
        Array.Resize(ref _childLayout, n);
        Array.Resize(ref _flags, n);
        Array.Resize(ref _style, n);
        Array.Resize(ref _node, n);
        Array.Resize(ref _parent, n);
        Array.Resize(ref _first, n);
        Array.Resize(ref _last, n);
        Array.Resize(ref _next, n);
        Array.Resize(ref _prev, n);
        Array.Resize(ref _nextPiece, n);
        Array.Resize(ref _oldStamp, n);
        Array.Resize(ref _fresh, n);
        if (_dirty.Length < n)
        {
            Array.Resize(ref _dirty, n);
            Array.Resize(ref _hazard, n);
        }
    }
}
