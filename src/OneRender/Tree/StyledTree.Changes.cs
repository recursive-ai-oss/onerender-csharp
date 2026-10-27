using OneRender.Interaction;
using OneRender.Style;
using ComponentValueList = RecursiveAI.OneHtml.Css.ComponentValueList;

namespace OneRender.Tree;

/// <summary>What happened to a node since the last flush; see <see cref="StyledTree.GetPendingChange"/>.</summary>
[Flags]
public enum NodeChanges : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The node's computed style changed.</summary>
    Style = 1,

    /// <summary>The node's text changed without changing whether it is empty or only collapsible white space.</summary>
    Text = 2,

    /// <summary>The node's text became empty or non-empty, or flipped between white space only and other text, so which boxes exist may differ.</summary>
    TextStructure = 4,

    /// <summary>The node's children were added, removed or reordered.</summary>
    Children = 8,

    /// <summary>The natural size or source of replaced content changed.</summary>
    Replaced = 16,

    /// <summary>The node was inserted (or moved to a new place).</summary>
    Added = 32,

    /// <summary>The node was removed (or moved away). It keeps its index but is not part of the tree any more.</summary>
    Removed = 64,
}

/// <summary>One entry of the pending change set of a <see cref="StyledTree"/>.</summary>
/// <param name="Node">The node.</param>
/// <param name="Kinds">Everything that happened to it since the last flush, merged.</param>
public readonly record struct PendingChange(StyledNode Node, NodeChanges Kinds);

/// <summary>
/// Computes the style of one node from its front end's inputs. The builder front end's own implementation reads the node's
/// <see cref="StyleDeclaration"/> layers; <see cref="HtmlFrontEnd"/> installs one that reads OneHtml's cascade.
/// </summary>
internal interface INodeStyler
{
    /// <summary>The style of an element for the given parent style.</summary>
    StyleId ComputeElement(StyledTree tree, int node, StyleId parentStyle);

    /// <summary>The style of a marker or generated-content node for the given parent style.</summary>
    StyleId ComputeOther(StyledTree tree, int node, StyleId parentStyle);
}

public sealed partial class StyledTree
{
    // Bit in _chg for "the node's style must be recomputed"; the low bits are NodeChanges.
    private const byte PendingStyleBit = 128;

    /// <summary>The most distinct nodes one change set holds before it gives up and asks for a full rebuild. Default 4,096.</summary>
    public const int MaxPendingChanges = 4096;

    private StyleDeclaration?[] _decl = new StyleDeclaration?[64];
    private StyleDeclaration?[]? _focusDecl;
    private StyleDeclaration?[]? _hoverDecl;
    private StyleDeclaration?[]? _activeDecl;
    private byte[] _states = new byte[64];
    private int[] _textCap = new int[64];
    private int[] _oldStyle = new int[64];
    private byte[] _chg = new byte[64];
    private int[] _chgList = new int[64];
    private int _chgCount;
    private bool _chgOverflow;
    private bool _tracking;
    private bool _forceRestyle;
    private int[] _work = new int[64];
    private char[] _textSpare = [];
    private int _structureEpoch;
    private INodeStyler? _styler;
    private readonly Dictionary<LayerTuple, LayerKey> _layers = new();
    private readonly LayeredSource _layered = new();

    private readonly record struct LayerTuple(object? Base, object? Focus, object? Hover, object? Active, byte Mask);

    private sealed class LayerKey
    {
        public int Version;
        public int BaseVersion = -1;
        public int FocusVersion = -1;
        public int HoverVersion = -1;
        public int ActiveVersion = -1;
    }

    /// <summary>
    /// Counts the changes that altered which nodes exist, their order or the white-space-only text boxes; consumers that keep
    /// per-structure data compare it with the value they saw last.
    /// </summary>
    public int StructureEpoch => _structureEpoch;

    /// <summary>The number of distinct nodes with a pending change.</summary>
    public int PendingCount => _chgCount;

    /// <summary>True when more nodes changed than <see cref="MaxPendingChanges"/> holds: the change set is incomplete and a consumer must rebuild everything.</summary>
    public bool PendingOverflowed => _chgOverflow;

    /// <summary>True when something is waiting for <see cref="Restyle"/> or for the consumers of the change set.</summary>
    public bool HasPending => _chgCount > 0 || _chgOverflow;

    /// <summary>Whether edits are being recorded. Off while a tree is built; <c>LivePage</c> switches it on.</summary>
    internal bool Tracking
    {
        get => _tracking;
        set => _tracking = value;
    }

    internal INodeStyler? Styler
    {
        get => _styler;
        set => _styler = value;
    }

    /// <summary>One pending change.</summary>
    /// <param name="index">0 to <see cref="PendingCount"/> - 1, in the order the nodes were first touched.</param>
    /// <returns>The node and what happened to it. Entries for style recomputation not yet done show <see cref="NodeChanges.None"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public PendingChange GetPendingChange(int index)
    {
        if ((uint)index >= (uint)_chgCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int n = _chgList[index];
        return new PendingChange(Make(n), (NodeChanges)(_chg[n] & 127));
    }

    /// <summary>The style a node had before its first change in the current change set.</summary>
    internal StyleId OldStyleOf(int node) => new(_oldStyle[node]);

    internal int PendingNode(int index) => _chgList[index];

    internal NodeChanges PendingKinds(int index) => (NodeChanges)(_chg[_chgList[index]] & 127);

    /// <summary>Forgets the pending changes after the consumers have taken them.</summary>
    internal void ClearPending()
    {
        if (_chgOverflow)
        {
            Array.Clear(_chg, 0, _count);
            _chgOverflow = false;
        }
        else
        {
            for (int i = 0; i < _chgCount; i++)
            {
                _chg[_chgList[i]] = 0;
            }
        }

        _chgCount = 0;
    }

    private void Note(int node, byte bits)
    {
        if (!_tracking || _chgOverflow)
        {
            return;
        }

        if (_chg[node] == 0)
        {
            if (_chgCount == MaxPendingChanges)
            {
                _chgOverflow = true;
                return;
            }

            if (_chgCount == _chgList.Length)
            {
                Array.Resize(ref _chgList, _chgList.Length * 2);
            }

            _chgList[_chgCount++] = node;
        }

        _chg[node] |= bits;
    }

    private void CheckNode(StyledNode node, string name)
    {
        if (node.IsNull || !ReferenceEquals(node.Tree, this))
        {
            throw new ArgumentException("The node does not belong to this tree.", name);
        }
    }

    // ---------------------------------------------------------------- builder edits

    /// <summary>
    /// Replaces the declarations of an element. The style is recomputed at the next flush; nothing is parsed or computed here.
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="declaration">The new declarations; null for none. The object may be shared by many nodes.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> does not belong to this tree or is not an element.</exception>
    public void SetDeclaration(StyledNode node, StyleDeclaration? declaration)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        _decl[node.Index] = declaration;
        MarkStyleDirty(node.Index);
    }

    /// <summary>The declarations the builder gave an element, or null.</summary>
    /// <param name="node">A node of this tree.</param>
    public StyleDeclaration? GetDeclaration(StyledNode node)
    {
        CheckNode(node, nameof(node));
        return _decl[node.Index];
    }

    /// <summary>
    /// Tells the tree that a <see cref="StyleDeclaration"/> it holds was changed (<see cref="StyleDeclaration.Set"/> and friends), so
    /// the element's style is recomputed at the next flush. Call it for every node that uses the declaration object.
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    public void Invalidate(StyledNode node)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        MarkStyleDirty(node.Index);
    }

    /// <summary>
    /// Sets one property of an element's own declaration (creating it when the element has none) and marks the style for recomputation.
    /// The declaration is edited in place, so call <see cref="Invalidate"/> for other nodes that share it.
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="property">A property name.</param>
    /// <param name="value">CSS text.</param>
    /// <returns>False when the declaration did not accept the value (see <see cref="StyleDeclaration.TrySet"/>).</returns>
    public bool SetProperty(StyledNode node, string property, string value)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        StyleDeclaration d = _decl[node.Index] ??= new StyleDeclaration();
        bool ok = d.TrySet(property, value);
        MarkStyleDirty(node.Index);
        return ok;
    }

    /// <summary>
    /// Declares what an element looks like while it is in a state, on top of its base declaration. When several states apply their
    /// declarations layer in the order focus, hover, active (active wins).
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="state">Exactly one of <see cref="ElementStates.Hover"/>, <see cref="ElementStates.Active"/> and <see cref="ElementStates.Focus"/>.</param>
    /// <param name="declaration">The declarations for the state; null removes them.</param>
    /// <exception cref="ArgumentException">The state is not one of the three.</exception>
    public void SetStateStyle(StyledNode node, ElementStates state, StyleDeclaration? declaration)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        StyleDeclaration?[] column = state switch
        {
            ElementStates.Hover => _hoverDecl ??= new StyleDeclaration?[_kind.Length],
            ElementStates.Active => _activeDecl ??= new StyleDeclaration?[_kind.Length],
            ElementStates.Focus => _focusDecl ??= new StyleDeclaration?[_kind.Length],
            _ => throw new ArgumentException("Give exactly one of Hover, Active or Focus.", nameof(state)),
        };
        column[node.Index] = declaration;
        if ((_states[node.Index] & (byte)state) != 0)
        {
            MarkStyleDirty(node.Index);
        }
    }

    /// <summary>Sets whether an element is in the given states. Only the states that have a declaration (<see cref="SetStateStyle"/>) change its style.</summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="states">The states the element is now in.</param>
    public void SetStates(StyledNode node, ElementStates states)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        int i = node.Index;
        byte now = (byte)(states & (ElementStates.Hover | ElementStates.Active | ElementStates.Focus));
        byte before = _states[i];
        if (before == now)
        {
            return;
        }

        _states[i] = now;
        byte diff = (byte)(before ^ now);
        if (((diff & (byte)ElementStates.Hover) != 0 && _hoverDecl?[i] is not null)
            || ((diff & (byte)ElementStates.Active) != 0 && _activeDecl?[i] is not null)
            || ((diff & (byte)ElementStates.Focus) != 0 && _focusDecl?[i] is not null))
        {
            MarkStyleDirty(i);
        }
    }

    private struct ControlInfo
    {
        public Forms.ControlKind Kind;
        public Forms.ControlStates State;
        public double Fraction;
    }

    private ControlInfo[]? _control;

    /// <summary>
    /// Makes an element a form control that is drawn natively (Chrome's default look) when its border and background are the
    /// defaults, or changes the state it is drawn in. The element's size and box come from its declarations as for any element; only
    /// the look depends on this. Nothing is allocated once the tree has grown to its size.
    /// </summary>
    /// <param name="node">An element of this tree.</param>
    /// <param name="kind">The control the element is; <see cref="Forms.ControlKind.None"/> makes it an ordinary element again.</param>
    /// <param name="states">Whether it is checked, disabled and so on.</param>
    /// <param name="fraction">For a range, progress or meter: how far along the track the value is, 0 to 1.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> does not belong to this tree or is not an element.</exception>
    public void SetControl(StyledNode node, Forms.ControlKind kind, Forms.ControlStates states = Forms.ControlStates.None, double fraction = 0)
    {
        CheckNode(node, nameof(node));
        CheckElement(node.Index);
        _control ??= new ControlInfo[_kind.Length];
        ref ControlInfo c = ref _control[node.Index];
        double f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        if (c.Kind == kind && c.State == states && c.Fraction.Equals(f))
        {
            return;
        }

        c.Kind = kind;
        c.State = states;
        c.Fraction = f;
        MarkStyleDirty(node.Index);
    }

    /// <summary>The control kind and state set with <see cref="SetControl"/>.</summary>
    /// <param name="node">A node of this tree.</param>
    /// <param name="states">The state flags.</param>
    /// <returns>The kind, <see cref="Forms.ControlKind.None"/> for an ordinary element.</returns>
    public Forms.ControlKind GetControl(StyledNode node, out Forms.ControlStates states)
    {
        CheckNode(node, nameof(node));
        if (_control is null)
        {
            states = Forms.ControlStates.None;
            return Forms.ControlKind.None;
        }

        states = _control[node.Index].State;
        return _control[node.Index].Kind;
    }

    /// <summary>The states set with <see cref="SetStates"/>.</summary>
    /// <param name="node">An element of this tree.</param>
    public ElementStates GetStates(StyledNode node)
    {
        CheckNode(node, nameof(node));
        return (ElementStates)_states[node.Index];
    }

    /// <summary>
    /// Replaces the text of a text node. When the new text fits where the old text was stored nothing is allocated.
    /// </summary>
    /// <param name="node">A text node of this tree.</param>
    /// <param name="text">The new text; see <see cref="AddText"/>.</param>
    /// <exception cref="ArgumentException">The node is not a text node of this tree.</exception>
    public void SetText(StyledNode node, ReadOnlySpan<char> text)
    {
        CheckNode(node, nameof(node));
        if (_kind[node.Index] != (byte)StyledNodeKind.Text)
        {
            throw new ArgumentException("Only text nodes have text.", nameof(node));
        }

        int i = node.Index;
        ReadOnlySpan<char> old = TextOf(i);
        if (old.SequenceEqual(text))
        {
            return;
        }

        // Which text boxes survive depends on whether a text is empty or only white space (and, under pre-line, which white space).
        bool structural = old.IsEmpty || text.IsEmpty || IsOnlyCollapsibleSpace(old) || IsOnlyCollapsibleSpace(text);
        if (text.Length <= _textCap[i])
        {
            text.CopyTo(_text.AsSpan(_textStart[i]));
            _textLength[i] = text.Length;
        }
        else
        {
            StoreText(i, text);
        }

        Note(i, (byte)(structural ? NodeChanges.TextStructure : NodeChanges.Text));
        if (structural)
        {
            _structureEpoch++;
        }
    }

    // White space that CSS Text 3 §4.1.1 can collapse away: spaces, tabs and line feeds (whether it does depends on white-space,
    // which the box tree checks; here only the text's shape matters, because a flip means boxes may appear or vanish).
    private static bool IsOnlyCollapsibleSpace(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (c is not (' ' or '\t' or '\n' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    private void StoreText(int i, ReadOnlySpan<char> text)
    {
        int cap = Math.Max(16, (text.Length + 15) & ~15);
        if (_textCount + cap > _text.Length)
        {
            CompactText(Math.Max(cap, 0));
        }

        if (_textCount + cap > _text.Length)
        {
            Array.Resize(ref _text, (int)Math.Min(Array.MaxLength, Math.Max((long)_text.Length * 2, (long)_textCount + cap)));
        }

        text.CopyTo(_text.AsSpan(_textCount));
        _textStart[i] = _textCount;
        _textLength[i] = text.Length;
        _textCap[i] = cap;
        _textCount += cap;
    }

    // Slots whose text moved leave holes. When the arena is full, live texts are packed into the spare buffer and the buffers swap,
    // so the arena stays about as big as the live text however often labels change.
    private void CompactText(int needed)
    {
        long live = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_textCap[i] > 0)
            {
                live += _textCap[i];
            }
        }

        if (live + needed > _text.Length * 3L / 4)
        {
            return; // mostly live: growing is cheaper than packing
        }

        if (_textSpare.Length < _text.Length)
        {
            _textSpare = new char[_text.Length];
        }

        int pos = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_textCap[i] <= 0)
            {
                continue;
            }

            _text.AsSpan(_textStart[i], _textLength[i]).CopyTo(_textSpare.AsSpan(pos));
            _textStart[i] = pos;
            pos += _textCap[i];
        }

        (_text, _textSpare) = (_textSpare, _text);
        _textCount = pos;
    }

    /// <summary>Appends a new element as a child of <paramref name="parent"/>, before <paramref name="before"/>.</summary>
    /// <param name="parent">An element of this tree.</param>
    /// <param name="before">A child of <paramref name="parent"/> to insert before; the null handle appends.</param>
    /// <param name="tag">The element's local name.</param>
    /// <param name="style">Declared values; null for none.</param>
    /// <returns>The new node, or the null handle when a limit stopped it.</returns>
    /// <exception cref="ArgumentException">A handle is from another tree, or <paramref name="before"/> is not a child of <paramref name="parent"/>.</exception>
    public StyledNode InsertElement(StyledNode parent, StyledNode before, string tag, StyleDeclaration? style = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        StyledNode n = AddElement(parent, tag, style);
        if (!n.IsNull && !before.IsNull)
        {
            MoveBefore(n, parent, before);
        }

        return n;
    }

    /// <summary>Adds text as a child of <paramref name="parent"/>, before <paramref name="before"/>.</summary>
    /// <param name="parent">An element of this tree.</param>
    /// <param name="before">A child of <paramref name="parent"/> to insert before; the null handle appends.</param>
    /// <param name="text">The text; empty text adds nothing.</param>
    /// <returns>The new node, or the null handle when nothing was added.</returns>
    public StyledNode InsertText(StyledNode parent, StyledNode before, ReadOnlySpan<char> text)
    {
        StyledNode n = AddText(parent, text);
        if (!n.IsNull && !before.IsNull)
        {
            MoveBefore(n, parent, before);
        }

        return n;
    }

    /// <summary>Takes a node and its subtree out of the tree. The nodes keep their indices but are no longer reachable.</summary>
    /// <param name="node">A non-root node of this tree.</param>
    /// <exception cref="ArgumentException">The node is the root, already removed, or from another tree.</exception>
    public void Remove(StyledNode node)
    {
        CheckNode(node, nameof(node));
        int i = node.Index;
        if (i == 0 || _parent[i] < 0)
        {
            throw new ArgumentException("The root and removed nodes cannot be removed.", nameof(node));
        }

        int p = _parent[i];
        Unlink(i);
        Note(i, (byte)NodeChanges.Removed);
        Note(p, (byte)NodeChanges.Children);
        _structureEpoch++;
    }

    /// <summary>Moves a node and its subtree to be a child of <paramref name="newParent"/>, before <paramref name="before"/>.</summary>
    /// <param name="node">A non-root node of this tree that is not an ancestor of <paramref name="newParent"/>.</param>
    /// <param name="newParent">An element of this tree.</param>
    /// <param name="before">A child of <paramref name="newParent"/>; the null handle moves to the end.</param>
    /// <exception cref="ArgumentException">A handle is from another tree, the move would make a node its own ancestor, or <paramref name="before"/> is not a child of <paramref name="newParent"/>.</exception>
    public void Move(StyledNode node, StyledNode newParent, StyledNode before = default)
    {
        CheckNode(node, nameof(node));
        CheckNode(newParent, nameof(newParent));
        CheckElement(newParent.Index);
        int i = node.Index;
        if (i == 0 || _parent[i] < 0)
        {
            throw new ArgumentException("The root and removed nodes cannot be moved.", nameof(node));
        }

        for (int a = newParent.Index; a >= 0; a = _parent[a])
        {
            if (a == i)
            {
                throw new ArgumentException("A node cannot move into its own subtree.", nameof(newParent));
            }
        }

        if (_replaced[newParent.Index] != 0)
        {
            throw new ArgumentException("Replaced elements take no children.", nameof(newParent));
        }

        int oldParent = _parent[i];
        Unlink(i);
        Note(oldParent, (byte)NodeChanges.Children);
        MoveBefore(node, newParent, before);
    }

    private void MoveBefore(StyledNode node, StyledNode parent, StyledNode before)
    {
        int i = node.Index;
        int p = parent.Index;
        if (!before.IsNull)
        {
            CheckNode(before, nameof(before));
            if (_parent[before.Index] != p)
            {
                throw new ArgumentException("The node to insert before is not a child of the parent.", nameof(before));
            }
        }

        if (_parent[i] >= 0)
        {
            Unlink(i);
        }

        _parent[i] = p;
        int b = before.IsNull ? -1 : before.Index;
        if (b < 0)
        {
            _next[i] = -1;
            if (_last[p] >= 0)
            {
                _next[_last[p]] = i;
            }
            else
            {
                _first[p] = i;
            }

            _last[p] = i;
        }
        else
        {
            int prev = PreviousOf(b);
            _next[i] = b;
            if (prev >= 0)
            {
                _next[prev] = i;
            }
            else
            {
                _first[p] = i;
            }
        }

        SetDepthBelow(i, _depth[p] + 1);
        Note(i, (byte)NodeChanges.Added);
        Note(p, (byte)NodeChanges.Children);
        _structureEpoch++;
        // A moved node may inherit different values from its new parent.
        MarkStyleDirty(i);
    }

    private int PreviousOf(int node)
    {
        int p = _parent[node];
        int c = _first[p];
        if (c == node)
        {
            return -1;
        }

        while (c >= 0 && _next[c] != node)
        {
            c = _next[c];
        }

        return c;
    }

    // Detaches a node from its parent's child list (no change recording).
    private void Unlink(int i)
    {
        int p = _parent[i];
        int prev = PreviousOf(i);
        int next = _next[i];
        if (prev >= 0)
        {
            _next[prev] = next;
        }
        else
        {
            _first[p] = next;
        }

        if (next < 0)
        {
            _last[p] = prev;
        }

        _parent[i] = -1;
        _next[i] = -1;
    }

    private void SetDepthBelow(int root, int depth)
    {
        EnsureWork(1);
        int sp = 0;
        _work[sp++] = root;
        _depth[root] = depth;
        while (sp > 0)
        {
            int n = _work[--sp];
            for (int c = _first[n]; c >= 0; c = _next[c])
            {
                _depth[c] = _depth[n] + 1;
                EnsureWork(sp + 1);
                _work[sp++] = c;
            }
        }
    }

    private void EnsureWork(int size)
    {
        if (size > _work.Length)
        {
            Array.Resize(ref _work, Math.Max(size, _work.Length * 2));
        }
    }

    private void CheckElement(int i)
    {
        if (_kind[i] != (byte)StyledNodeKind.Element)
        {
            throw new ArgumentException("The node is not an element.");
        }
    }

    // ---------------------------------------------------------------- restyle

    // Recorded even before tracking starts, so declarations set while a tree is built are honored when a page takes it over.
    internal void MarkStyleDirty(int node)
    {
        if ((_chg[node] & PendingStyleBit) != 0)
        {
            return;
        }

        if (_chgOverflow)
        {
            _chg[node] |= PendingStyleBit;
            return;
        }

        if (_chg[node] == 0)
        {
            if (_chgCount == MaxPendingChanges)
            {
                _chgOverflow = true;
                _chg[node] |= PendingStyleBit;
                return;
            }

            if (_chgCount == _chgList.Length)
            {
                Array.Resize(ref _chgList, _chgList.Length * 2);
            }

            _chgList[_chgCount++] = node;
        }

        _chg[node] |= PendingStyleBit;
    }

    /// <summary>
    /// Recomputes the styles of the nodes marked for it and of the descendants whose inherited values changed. A node whose
    /// recomputed style equals its old one stops the walk there. Changes are recorded as <see cref="NodeChanges.Style"/>.
    /// </summary>
    /// <returns>True when any style changed.</returns>
    internal bool Restyle()
    {
        if (_chgOverflow)
        {
            RestyleAll();
            return true;
        }

        bool any = false;
        ComputeEnvironment rootFont = Styles.RootEnvironment;
        for (int k = 0; k < _chgCount; k++)
        {
            int n = _chgList[k];
            if ((_chg[n] & PendingStyleBit) == 0)
            {
                continue;
            }

            _chg[n] = (byte)(_chg[n] & ~PendingStyleBit);
            if (_parent[n] < 0 && n != 0)
            {
                continue; // removed while waiting
            }

            any |= RecomputeAndPropagate(n);
        }

        if (Styles.RootEnvironment != rootFont)
        {
            // rem, rex, rcap, rch and ric lengths everywhere depend on the root's font.
            RestyleAll();
            return true;
        }

        return any;
    }

    // Recomputes one node and, while styles keep changing, its descendants (explicit stack; no recursion on depth).
    private bool RecomputeAndPropagate(int start)
    {
        bool any = false;
        int sp = 0;
        EnsureWork(1);
        _work[sp++] = start;
        while (sp > 0)
        {
            int n = _work[--sp];
            if (!Recompute(n))
            {
                continue;
            }

            any = true;
            EnsureWork(sp + 1);
            for (int c = _first[n]; c >= 0; c = _next[c])
            {
                EnsureWork(sp + 1);
                _work[sp++] = c;
            }
        }

        return any;
    }

    // Recomputes every node from the root down, recording changes.
    internal void RestyleAll(bool force = false)
    {
        _forceRestyle = force;
        Array.Clear(_chg, 0, _count);
        _chgCount = 0;
        _chgOverflow = true;
        int sp = 0;
        EnsureWork(1);
        _work[sp++] = 0;
        while (sp > 0)
        {
            int n = _work[--sp];
            Recompute(n);
            for (int c = _first[n]; c >= 0; c = _next[c])
            {
                EnsureWork(sp + 1);
                _work[sp++] = c;
            }
        }

        _forceRestyle = false;
    }

    // Returns whether the node's style id changed.
    private bool Recompute(int n)
    {
        int p = _parent[n];
        StyleId parentStyle = p < 0 ? default : new StyleId(_style[p]);
        StyleId next;
        switch ((StyledNodeKind)_kind[n])
        {
            case StyledNodeKind.Text:
                next = parentStyle;
                break;
            case StyledNodeKind.Marker:
                next = _styler is not null ? _styler.ComputeOther(this, n, parentStyle) : Styles.GetAnonymous(parentStyle, DisplayType.Inline);
                break;
            case StyledNodeKind.Element:
                next = _styler is not null ? _styler.ComputeElement(this, n, parentStyle) : ComputeBuilderElement(n, parentStyle);
                if (p < 0)
                {
                    Styles.NoteRoot(next);
                }

                break;
            default:
                next = _styler is not null ? _styler.ComputeOther(this, n, parentStyle) : new StyleId(_style[n]);
                break;
        }

        if (next.Index == _style[n] && !_forceRestyle)
        {
            return false;
        }

        if ((_chg[n] & (byte)NodeChanges.Style) == 0)
        {
            _oldStyle[n] = _style[n];
        }

        _style[n] = next.Index;
        Note(n, (byte)NodeChanges.Style);
        if ((StyledNodeKind)_kind[n] == StyledNodeKind.Element && _styler is null)
        {
            EnsureMarker(n);
        }

        return true;
    }

    // Builder trees give a list item its marker when the element is made; a style change can turn an element into a list item or
    // out of one, and a fresh build of the same content would differ unless the marker follows.
    private void EnsureMarker(int n)
    {
        bool listItem = Styles[new StyleId(_style[n])].Display.IsListItem;
        int first = _first[n];
        bool hasMarker = first >= 0 && _kind[first] == (byte)StyledNodeKind.Marker;
        if (listItem && !hasMarker && _replaced[n] == 0)
        {
            int m = NewNode(StyledNodeKind.Marker, n, Styles.GetAnonymous(new StyleId(_style[n]), DisplayType.Inline), -1);
            if (m >= 0)
            {
                // NewNode appended it; a marker is always the first child.
                Unlink(m);
                _parent[m] = n;
                _next[m] = _first[n];
                if (_first[n] < 0)
                {
                    _last[n] = m;
                }

                _first[n] = m;
                Note(m, (byte)NodeChanges.Added);
                Note(n, (byte)NodeChanges.Children);
                _structureEpoch++;
            }
        }
        else if (!listItem && hasMarker)
        {
            Unlink(first);
            Note(first, (byte)NodeChanges.Removed);
            Note(n, (byte)NodeChanges.Children);
            _structureEpoch++;
        }
    }

    private StyleId ComputeBuilderElement(int n, StyleId parentStyle)
    {
        StyleId style = ComputeBuilderLayers(n, parentStyle);
        if (_control is null || _control[n].Kind == Forms.ControlKind.None)
        {
            return style;
        }

        ControlInfo c = _control[n];
        ComputedValues v = Styles[style];
        v.Control = c.Kind;
        v.ControlState = c.State;
        v.ControlFraction = c.Fraction;
        return Styles.Derive(style, in v);
    }

    private StyleId ComputeBuilderLayers(int n, StyleId parentStyle)
    {
        int p = _parent[n];
        StyleDeclaration? decl = _decl[n];
        byte st = _states[n];
        StyleDeclaration? focus = (st & (byte)ElementStates.Focus) != 0 ? _focusDecl?[n] : null;
        StyleDeclaration? hover = (st & (byte)ElementStates.Hover) != 0 ? _hoverDecl?[n] : null;
        StyleDeclaration? active = (st & (byte)ElementStates.Active) != 0 ? _activeDecl?[n] : null;
        if (focus is null && hover is null && active is null)
        {
            object key = (object?)decl ?? NoDeclaration;
            int version = decl?.Version ?? 0;
            if (Styles.TryGetCached(key, version, parentStyle, out StyleId cached))
            {
                return cached;
            }

            _builderSource.Declaration = decl;
            return Styles.Compute(key, version, _builderSource, parentStyle, p >= 0, p < 0);
        }

        var tuple = new LayerTuple(decl, focus, hover, active, (byte)((focus is null ? 0 : 1) | (hover is null ? 0 : 2) | (active is null ? 0 : 4)));
        if (!_layers.TryGetValue(tuple, out LayerKey? layer))
        {
            layer = new LayerKey();
            _layers[tuple] = layer;
        }

        int bv = decl?.Version ?? 0, fv = focus?.Version ?? 0, hv = hover?.Version ?? 0, av = active?.Version ?? 0;
        if (layer.BaseVersion != bv || layer.FocusVersion != fv || layer.HoverVersion != hv || layer.ActiveVersion != av)
        {
            layer.BaseVersion = bv;
            layer.FocusVersion = fv;
            layer.HoverVersion = hv;
            layer.ActiveVersion = av;
            layer.Version++;
        }

        if (Styles.TryGetCached(layer, layer.Version, parentStyle, out StyleId hit))
        {
            return hit;
        }

        _layered.Set(decl, focus, hover, active);
        return Styles.Compute(layer, layer.Version, _layered, parentStyle, p >= 0, p < 0);
    }

    /// <summary>A declaration source that reads several declarations, the last one that declares a property winning.</summary>
    private sealed class LayeredSource : DeclarationSource
    {
        private StyleDeclaration? _a, _b, _c, _d;

        public void Set(StyleDeclaration? a, StyleDeclaration? b, StyleDeclaration? c, StyleDeclaration? d)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
        }

        public override ValueOrigin Get(PropertyId id, out ComponentValueList value)
        {
            ValueOrigin o;
            if (_d is not null && (o = _d.Get(id, out value)) != ValueOrigin.Unset)
            {
                return o;
            }

            if (_c is not null && (o = _c.Get(id, out value)) != ValueOrigin.Unset)
            {
                return o;
            }

            if (_b is not null && (o = _b.Get(id, out value)) != ValueOrigin.Unset)
            {
                return o;
            }

            if (_a is not null)
            {
                return _a.Get(id, out value);
            }

            value = default;
            return ValueOrigin.Unset;
        }
    }
}
