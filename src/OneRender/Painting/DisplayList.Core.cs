using System.Numerics;

namespace OneRender.Painting;

public sealed partial class DisplayList
{
    private const ulong HashSeed = 0x6f72646cUL;

    internal string? StringAt(int oneBased) => oneBased <= 0 ? null : _strings[oneBased - 1];

    // ---------------------------------------------------------------- adding items

    private static bool IsPush(ItemKind k) =>
        k is ItemKind.PushClipRect or ItemKind.PushClipRoundedRect or ItemKind.PushClipPath or ItemKind.PushTransform or ItemKind.PushGroup or ItemKind.BeginTag
            or ItemKind.PushTransform3D or ItemKind.PushFilter or ItemKind.PushMask or ItemKind.PushMaskLayer or ItemKind.PushHostScope;

    private static bool IsPop(ItemKind k) =>
        k is ItemKind.PopClip or ItemKind.PopTransform or ItemKind.PopGroup or ItemKind.EndTag
            or ItemKind.PopFilter or ItemKind.PopMask or ItemKind.PopMaskLayer or ItemKind.PopHostScope;

    private static int FamilyOf(ItemKind push) => push switch
    {
        ItemKind.PushClipRect or ItemKind.PushClipRoundedRect or ItemKind.PushClipPath => 0,
        ItemKind.PushTransform or ItemKind.PushTransform3D => 1,
        ItemKind.PushGroup => 2,
        ItemKind.PushFilter => 4,
        ItemKind.PushMask => 5,
        ItemKind.PushMaskLayer => 6,
        ItemKind.PushHostScope => 7,
        _ => 3,
    };

    private void Begin(ItemKind kind, ItemId id, in RectF local, int flags, int numReserve)
    {
        if (!id.IsValid)
        {
            throw new DisplayListException("Item id 0 is reserved.");
        }

        if (_ow >= 0)
        {
            BeginStaged(kind, id, in local, flags, numReserve);
            return;
        }

        if (_count >= DisplayListLimits.MaxItems)
        {
            throw new DisplayListLimitException("A list holds at most " + DisplayListLimits.MaxItems + " items.");
        }

        if (IsPush(kind) && _depth >= DisplayListLimits.MaxNestingDepth)
        {
            throw new DisplayListLimitException("Clips, transforms, groups and tags nest at most " + DisplayListLimits.MaxNestingDepth + " deep.");
        }

        if (Find(id.Value) >= 0)
        {
            throw new DisplayListException("Item id " + id.Value + " is used twice in one list.");
        }

        Grow(ref _items, _count + 1);
        Grow(ref _num, _numCount + numReserve);
        Insert(id.Value, _count);
        _hash = Mix(HashSeed, (ulong)kind);
        _items[_count] = new ItemRecord
        {
            Kind = kind,
            Flags = flags,
            Id = id.Value,
            Local = local,
            Num = _numCount,
            Parent = _top,
            Match = -1,
            State = -1,
        };
        _count++;
    }

    private PushState StateOf(int pushIndex) =>
        pushIndex < 0
            ? new PushState { Ctm = Matrix3x2.Identity, Clip = RectF.Unbounded, Ctx = 0x726f6f74UL, Outer = Matrix4x4.Identity }
            : _states[_items[pushIndex].State];

    private void Finish(int idx)
    {
        ref ItemRecord rec = ref _items[idx];
        rec.ContentHash = _hash;
        rec.NumN = _numCount - rec.Num;
        PushState st = StateOf(rec.Parent);
        rec.CtxHash = st.Ctx;
        SetBounds(ref rec, in st);
        if (_ow >= 0)
        {
            CommitStaged(idx, false);
        }
    }

    private void FinishPush(int idx)
    {
        ref ItemRecord rec = ref _items[idx];
        rec.ContentHash = _hash;
        rec.NumN = _numCount - rec.Num;
        PushState parent = StateOf(rec.Parent);
        rec.CtxHash = parent.Ctx;
        if (_ow >= 0)
        {
            rec.State = _owSlot >= 0 ? _items[_owSlot].State : -1;
            PushState s = ComputePush(idx, parent);
            if (_owSlot >= 0)
            {
                _states[rec.State] = s;
            }

            CommitStaged(idx, true);
            return;
        }

        Grow(ref _states, _stateCount + 1);
        rec.State = _stateCount;
        _states[_stateCount++] = ComputePush(idx, parent);
        _top = idx;
        _depth++;
    }

    private PushState ComputePush(int idx, in PushState parent)
    {
        ref ItemRecord rec = ref _items[idx];
        PushState s = parent;
        switch (rec.Kind)
        {
            case ItemKind.PushClipRect:
            case ItemKind.PushClipRoundedRect:
            case ItemKind.PushClipPath:
                s.Clip = RectF.Intersect(parent.Clip, Project(rec.Local.Transform(parent.Ctm), in parent));
                s.Ctx = Mix(parent.Ctx, rec.ContentHash);
                break;
            case ItemKind.PushTransform:
                {
                    int n = rec.Num;
                    Matrix3x2 m = new(_num[n], _num[n + 1], _num[n + 2], _num[n + 3], _num[n + 4], _num[n + 5]);
                    s.Ctm = m * parent.Ctm;
                    s.Ctx = Mix(parent.Ctx, rec.ContentHash);
                    break;
                }

            case ItemKind.PushGroup:
            case ItemKind.PushMask:
            case ItemKind.PushMaskLayer:
            case ItemKind.PushHostScope:
                s.Ctx = Mix(parent.Ctx, rec.ContentHash);
                break;
            case ItemKind.PushFilter:
                {
                    // Blur and shadow spread ink beyond the items, so the clips outside the filter are widened by the same amount.
                    float outset = _num[rec.Num];
                    s.Expand = parent.Expand + outset;
                    s.Clip = Inflate(parent.Clip, outset);
                    s.Ctx = Mix(parent.Ctx, rec.ContentHash);
                    break;
                }

            case ItemKind.PushTransform3D:
                {
                    int n = rec.Num;
                    var m = new Matrix4x4(_num[n], _num[n + 1], _num[n + 2], _num[n + 3], _num[n + 4], _num[n + 5], _num[n + 6], _num[n + 7], _num[n + 8], _num[n + 9], _num[n + 10], _num[n + 11], _num[n + 12], _num[n + 13], _num[n + 14], _num[n + 15]);
                    var c = new Matrix4x4(parent.Ctm.M11, parent.Ctm.M12, 0, 0, parent.Ctm.M21, parent.Ctm.M22, 0, 0, 0, 0, 1, 0, parent.Ctm.M31, parent.Ctm.M32, 0, 1);
                    s.Outer = m * c * parent.Outer;
                    s.Has3D = true;
                    s.Ctm = Matrix3x2.Identity;
                    s.Ctx = Mix(parent.Ctx, rec.ContentHash);
                    break;
                }
            default:
                // Tags carry meaning, not pixels: they leave the context hash alone so that changing one never causes damage.
                break;
        }

        return s;
    }

    internal int TopIndex => _top;

    internal bool TopIsTag => _top >= 0 && _items[_top].Kind == ItemKind.BeginTag;

    private void Pop(ItemKind popKind, ItemKind pushKind)
    {
        if (_ow >= 0)
        {
            PopStaged(popKind, pushKind);
            return;
        }

        // Tags carry no pixels and nest only for the sake of structure, so closing any other scope first closes the tags opened inside
        // it: a painter that opens a tag lazily, when content needs it, never has to track which scope it is in.
        if (popKind != ItemKind.EndTag)
        {
            // A pop that will be refused leaves the list as it was: look past the tags before closing any of them.
            int scope = _top;
            while (scope >= 0 && _items[scope].Kind == ItemKind.BeginTag)
            {
                scope = _items[scope].Parent;
            }

            if (scope < 0 || FamilyOf(_items[scope].Kind) != FamilyOf(pushKind))
            {
                throw new DisplayListException(popKind + " does not match the innermost open push.");
            }

            while (_top >= 0 && _items[_top].Kind == ItemKind.BeginTag)
            {
                Pop(ItemKind.EndTag, ItemKind.BeginTag);
            }
        }

        int family = FamilyOf(pushKind);
        int top = _top;
        if (top < 0 || FamilyOf(_items[top].Kind) != family)
        {
            throw new DisplayListException(popKind + " does not match the innermost open push.");
        }

        if (_count >= DisplayListLimits.MaxItems)
        {
            throw new DisplayListLimitException("A list holds at most " + DisplayListLimits.MaxItems + " items.");
        }

        Grow(ref _items, _count + 1);
        int parent = _items[top].Parent;
        PushState ps = StateOf(parent);
        _items[_count] = new ItemRecord
        {
            Kind = popKind,
            ContentHash = Mix(HashSeed, (ulong)popKind),
            CtxHash = ps.Ctx,
            Num = _numCount,
            Parent = parent,
            Match = top,
            State = -1,
        };
        _items[top].Match = _count;
        _count++;
        _top = parent;
        _depth--;
    }

    // ---------------------------------------------------------------- in-place rewriting
    //
    // A painter that knows which part of the list is stale can write the same items again over their old slots: the item sequence
    // (kinds, ids, nesting) must come out the same, only payloads and hashes may change. New items are built at the tail as usual (so
    // every Draw/Push method works unchanged) and are then moved into the slot of the item they replace; the damage of each moved
    // item is collected on the way. Payload tables (numbers, glyphs, text, paths) only grow while rewriting; a full rebuild
    // (Clear) reclaims the space. A mismatch marks the rewrite failed and the caller paints the whole list again.

    private int _ow = -1;
    private int _owEnd;
    private int _owSlot;
    private bool _owFail;
    private int _owSavedTop;
    private int _owSavedDepth;
    private DamageRegion? _owDamage;
    private int[] _owRewalk = new int[16];
    private int _owRewalkCount;

    /// <summary>Payload entries (numbers, glyphs, text, paths, gradients, states) in use; a rewriting caller compares it with the size of the last full build to decide when to rebuild.</summary>
    internal long PayloadSize => (long)_numCount + _glyphCount + _textCount + _coordCount + _stopCount + _stateCount + _gradientCount + _stringCount;

    /// <summary>Makes room for rewriting to append twice what the list holds, so rewriting does not allocate until a full rebuild is due.</summary>
    internal void ReserveHeadroom()
    {
        Grow(ref _items, _count + 8);
        Grow(ref _num, (2 * _numCount) + 256);
        Grow(ref _glyphIds, (2 * _glyphCount) + 256);
        Grow(ref _glyphX, (2 * _glyphCount) + 256);
        Grow(ref _glyphY, (2 * _glyphCount) + 256);
        Grow(ref _glyphClusters, (2 * _glyphCount) + 256);
        Grow(ref _text, (2 * _textCount) + 256);
        Grow(ref _verbs, (2 * _verbCount) + 256);
        Grow(ref _coords, (2 * _coordCount) + 256);
        Grow(ref _paths, (2 * _pathCount) + 64);
        Grow(ref _gradients, (2 * _gradientCount) + 8);
        Grow(ref _stops, (2 * _stopCount) + 64);
        Grow(ref _strings, (2 * _stringCount) + 16);
    }

    /// <summary>True while a rewrite is open.</summary>
    internal bool Rewriting => _ow >= 0;

    /// <summary>Starts rewriting the items <paramref name="start"/> to <paramref name="start"/> + <paramref name="count"/> - 1.</summary>
    internal void BeginRewrite(int start, int count, DamageRegion damage)
    {
        _owSavedTop = _top;
        _owSavedDepth = _depth;
        _owFail = start < 0 || count < 0 || start + count > _count;
        _top = count > 0 && !_owFail ? _items[start].Parent : _top;
        _ow = start;
        _owEnd = start + count;
        _owSlot = -1;
        _owDamage = damage;
        _owRewalkCount = 0;
    }

    /// <summary>Ends a rewrite. True when the items came out in the same kinds, ids and nesting and filled the range exactly.</summary>
    internal bool EndRewrite()
    {
        bool ok = !_owFail && _ow == _owEnd && _count >= 0;
        _ow = -1;
        _top = _owSavedTop;
        _depth = _owSavedDepth;
        if (ok)
        {
            for (int k = 0; k < _owRewalkCount; k++)
            {
                RewalkDamage(_owRewalk[k], _owDamage!);
            }
        }

        _owRewalkCount = 0;
        return ok;
    }

    private void BeginStaged(ItemKind kind, ItemId id, in RectF local, int flags, int numReserve)
    {
        Grow(ref _items, _count + 1);
        Grow(ref _num, _numCount + numReserve);
        int slot = -1;
        if (!_owFail && _ow < _owEnd)
        {
            ref ItemRecord o = ref _items[_ow];
            if (o.Id == id.Value && o.Kind == kind && o.Parent == _top)
            {
                slot = _ow++;
            }
        }

        if (slot < 0)
        {
            _owFail = true;
        }

        _owSlot = slot;
        _hash = Mix(HashSeed, (ulong)kind);
        _items[_count] = new ItemRecord
        {
            Kind = kind,
            Flags = flags,
            Id = id.Value,
            Local = local,
            Num = _numCount,
            Parent = _top,
            Match = -1,
            State = -1,
        };
        _count++;
    }

    // Moves the item built at the tail into the slot it replaces and records what changed.
    private void CommitStaged(int idx, bool push)
    {
        int slot = _owSlot;
        if (slot >= 0 && !_owFail)
        {
            ref ItemRecord o = ref _items[slot];
            ref ItemRecord n = ref _items[idx];
            bool changed = o.ContentHash != n.ContentHash || o.CtxHash != n.CtxHash || o.Device != n.Device;
            if (changed)
            {
                _owDamage!.Add(o.Device);
                _owDamage.Add(n.Device);
                if (push)
                {
                    if (_owRewalkCount == _owRewalk.Length)
                    {
                        Array.Resize(ref _owRewalk, _owRewalk.Length * 2);
                    }

                    _owRewalk[_owRewalkCount++] = slot;
                }
            }

            n.Match = o.Match;
            n.State = o.State;
            ReusePayload(ref o, ref n);
            o = n;
            if (push)
            {
                _top = slot;
                _depth++;
            }
        }

        _count--;
    }

    // The new item's payload sits at the tails of the tables. When it has the size of the old item's, it goes over the old payload and
    // the tails shrink back, so a rewrite that changes values but not sizes does not grow the list.
    private void ReusePayload(ref ItemRecord o, ref ItemRecord n)
    {
        if (n.NumN == o.NumN && n.Num + n.NumN == _numCount)
        {
            Array.Copy(_num, n.Num, _num, o.Num, n.NumN);
            _numCount = n.Num;
            n.Num = o.Num;
        }

        if (n.Kind == ItemKind.GlyphRun && n.C == o.C && n.E == o.E && n.B + n.C == _glyphCount && n.D + n.E == _textCount)
        {
            Array.Copy(_glyphIds, n.B, _glyphIds, o.B, n.C);
            Array.Copy(_glyphX, n.B, _glyphX, o.B, n.C);
            Array.Copy(_glyphY, n.B, _glyphY, o.B, n.C);
            Array.Copy(_glyphClusters, n.B, _glyphClusters, o.B, n.C);
            Array.Copy(_text, n.D, _text, o.D, n.E);
            _glyphCount = n.B;
            _textCount = n.D;
            n.B = o.B;
            n.D = o.D;
        }
    }

    private void PopStaged(ItemKind popKind, ItemKind pushKind)
    {
        int top = _top;
        if (_owFail || _ow >= _owEnd || top < 0 || FamilyOf(_items[top].Kind) != FamilyOf(pushKind))
        {
            _owFail = true;
            return;
        }

        ref ItemRecord o = ref _items[_ow];
        if (o.Kind != popKind || o.Match != top)
        {
            _owFail = true;
            return;
        }

        int parent = _items[top].Parent;
        o.CtxHash = StateOf(parent).Ctx;
        _ow++;
        _top = parent;
        _depth--;
    }

    // Recomputes the state of a push whose payload changed and everything inside it, adding what moved to the damage.
    private void RewalkDamage(int push, DamageRegion damage)
    {
        int end = _items[push].Match;
        _states[_items[push].State] = ComputePush(push, StateOf(_items[push].Parent));
        for (int j = push + 1; j < end; j++)
        {
            ref ItemRecord rec = ref _items[j];
            PushState ps = StateOf(rec.Parent);
            ulong oldCtx = rec.CtxHash;
            RectF oldDevice = rec.Device;
            rec.CtxHash = ps.Ctx;
            if (IsPush(rec.Kind))
            {
                _states[rec.State] = ComputePush(j, ps);
            }
            else if (!IsPop(rec.Kind))
            {
                SetBounds(ref rec, in ps);
                if (oldCtx != rec.CtxHash || oldDevice != rec.Device)
                {
                    damage.Add(oldDevice);
                    damage.Add(rec.Device);
                }
            }
        }
    }

    // ---------------------------------------------------------------- id table

    private static int Slot(ulong id) => (int)((id * 0x9E3779B97F4A7C15UL) >> 33);

    private int Find(ulong id)
    {
        if (id == 0)
        {
            return -1;
        }

        int mask = _table.Length - 1;
        int h = Slot(id) & mask;
        while (true)
        {
            int v = _table[h];
            if (v == 0)
            {
                return -1;
            }

            if (_items[v - 1].Id == id)
            {
                return v - 1;
            }

            h = (h + 1) & mask;
        }
    }

    private void Insert(ulong id, int index)
    {
        if ((index + 1) * 2 > _table.Length)
        {
            int size = _table.Length;
            while (size < (index + 2) * 2)
            {
                size *= 2;
            }

            _table = new int[size];
            for (int i = 0; i < index; i++)
            {
                if (_items[i].Id != 0)
                {
                    Place(_items[i].Id, i);
                }
            }
        }

        Place(id, index);
    }

    private void Place(ulong id, int index)
    {
        int mask = _table.Length - 1;
        int h = Slot(id) & mask;
        while (_table[h] != 0)
        {
            h = (h + 1) & mask;
        }

        _table[h] = index + 1;
    }

    // ---------------------------------------------------------------- patching

    /// <summary>
    /// Replaces the matrix of a <see cref="ItemKind.PushTransform"/> in place, without rebuilding the list. Items inside the
    /// transform get new root bounds, and the old and new areas are added to <paramref name="damage"/>. Allocates nothing.
    /// </summary>
    /// <param name="id">The id of the transform item.</param>
    /// <param name="matrix">The new matrix.</param>
    /// <param name="damage">Receives the damaged areas; may be null.</param>
    /// <exception cref="ArgumentException">The list has no such item, or it is not a transform, or it is not closed yet.</exception>
    public void PatchTransform(ItemId id, in Matrix3x2 matrix, DamageRegion? damage = null)
    {
        CheckMatrix(matrix);
        int i = FindClosed(id, ItemKind.PushTransform);
        AddRange(i, damage);
        int n = _items[i].Num;
        _num[n] = matrix.M11;
        _num[n + 1] = matrix.M12;
        _num[n + 2] = matrix.M21;
        _num[n + 3] = matrix.M22;
        _num[n + 4] = matrix.M31;
        _num[n + 5] = matrix.M32;
        ulong h = Mix(HashSeed, (ulong)ItemKind.PushTransform);
        for (int k = 0; k < 6; k++)
        {
            h = Mix(h, Bits(_num[n + k]));
        }

        _items[i].ContentHash = h;
        Rewalk(i);
        AddRange(i, damage);
    }

    /// <summary>
    /// Replaces the opacity of a <see cref="ItemKind.PushGroup"/> in place. The group's items keep their bounds; they are added to
    /// <paramref name="damage"/> because their pixels change. Allocates nothing.
    /// </summary>
    /// <param name="id">The id of the group item.</param>
    /// <param name="opacity">The new opacity, 0 to 1.</param>
    /// <param name="damage">Receives the damaged areas; may be null.</param>
    /// <exception cref="ArgumentException">The list has no such item, or it is not a group, or it is not closed yet.</exception>
    public void PatchOpacity(ItemId id, float opacity, DamageRegion? damage = null)
    {
        CheckOpacity(opacity);
        int i = FindClosed(id, ItemKind.PushGroup);
        AddRange(i, damage);
        int n = _items[i].Num;
        _num[n] = opacity;
        ulong h = Mix(HashSeed, (ulong)ItemKind.PushGroup);
        h = Mix(h, Bits(opacity));
        h = Mix(h, (ulong)_items[i].A);
        _items[i].ContentHash = h;
        Rewalk(i);
        AddRange(i, damage);
    }

    private int FindClosed(ItemId id, ItemKind kind)
    {
        int i = Find(id.Value);
        if (i < 0 || _items[i].Kind != kind)
        {
            throw new ArgumentException("The list has no " + kind + " item with that id.", nameof(id));
        }

        if (_items[i].Match < 0)
        {
            throw new ArgumentException("The item is still open; close it before patching.", nameof(id));
        }

        return i;
    }

    private void AddRange(int push, DamageRegion? damage)
    {
        if (damage is null)
        {
            return;
        }

        int end = _items[push].Match;
        for (int j = push + 1; j < end; j++)
        {
            damage.Add(_items[j].Device);
        }
    }

    // Recomputes the context state of a push and of everything inside it after its payload changed.
    private void Rewalk(int push)
    {
        int end = _items[push].Match;
        _states[_items[push].State] = ComputePush(push, StateOf(_items[push].Parent));
        for (int j = push + 1; j < end; j++)
        {
            ref ItemRecord rec = ref _items[j];
            PushState ps = StateOf(rec.Parent);
            rec.CtxHash = ps.Ctx;
            if (IsPush(rec.Kind))
            {
                _states[rec.State] = ComputePush(j, ps);
            }
            else if (!IsPop(rec.Kind))
            {
                SetBounds(ref rec, in ps);
            }
        }
    }

    // ---------------------------------------------------------------- damage

    /// <summary>
    /// Adds to <paramref name="damage"/> the area where <paramref name="next"/> differs from <paramref name="previous"/>.
    /// Items are matched by id. A changed item (different content, or a different enclosing transform, clip or group) adds its old
    /// and new root bounds; an added item adds its bounds; a removed item adds its old bounds; an item that kept its content but
    /// moved earlier in paint order relative to other kept items adds both bounds, because what it overlaps changed.
    /// Allocates nothing once both lists have been used at this size.
    /// </summary>
    /// <param name="previous">The version the viewer currently shows.</param>
    /// <param name="next">The new version.</param>
    /// <param name="damage">Receives the changed areas in root space (CSS pixels).</param>
    public static void ComputeDamage(DisplayList previous, DisplayList next, DamageRegion damage)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(damage);
        Grow(ref previous._seen, previous._count);
        Array.Clear(previous._seen, 0, previous._count);
        int maxOld = -1;
        for (int i = 0; i < next._count; i++)
        {
            ref ItemRecord n = ref next._items[i];
            if (n.Id == 0)
            {
                continue;
            }

            int j = previous.Find(n.Id);
            if (j < 0)
            {
                damage.Add(n.Device);
                continue;
            }

            previous._seen[j] = 1;
            ref ItemRecord p = ref previous._items[j];
            bool same = p.Kind == n.Kind && p.ContentHash == n.ContentHash && p.CtxHash == n.CtxHash && p.Device == n.Device;
            bool inOrder = j > maxOld;
            if (inOrder)
            {
                maxOld = j;
            }

            if (!same || !inOrder)
            {
                damage.Add(p.Device);
                damage.Add(n.Device);
            }
        }

        for (int j = 0; j < previous._count; j++)
        {
            if (previous._seen[j] == 0 && previous._items[j].Id != 0)
            {
                damage.Add(previous._items[j].Device);
            }
        }
    }
}

/// <summary>
/// Two display lists used in turn: the one on screen and the one being built. <see cref="Begin"/> hands out the cleared back
/// list, <see cref="Commit"/> diffs it against the front list, swaps them and collects the damage. Everything is pooled, so once
/// both lists have grown to the frame's size a rebuild allocates nothing.
/// </summary>
public sealed class RetainedDisplayList
{
    private DisplayList _front = new();
    private DisplayList _back = new();
    private bool _building;

    /// <summary>The committed list. Empty before the first <see cref="Commit"/>.</summary>
    public DisplayList Current => _front;

    /// <summary>
    /// Everything that changed since <see cref="ClearDamage"/> was last called: the diffs of commits and the effect of patches.
    /// The first commit damages the whole list.
    /// </summary>
    public DamageRegion Damage { get; } = new();

    /// <summary>Clears the back list and returns it for the painter to fill.</summary>
    /// <returns>An empty list. Fill it, then call <see cref="Commit"/>.</returns>
    /// <exception cref="InvalidOperationException">The previous list was never committed.</exception>
    public DisplayList Begin()
    {
        if (_building)
        {
            throw new InvalidOperationException("Commit the list from the previous Begin first.");
        }

        _back.Clear();
        _building = true;
        return _back;
    }

    /// <summary>Validates the list from <see cref="Begin"/>, adds its difference from the current list to <see cref="Damage"/>, and makes it current.</summary>
    /// <exception cref="InvalidOperationException">No list was begun.</exception>
    /// <exception cref="DisplayListException">The list has unclosed pushes.</exception>
    public void Commit()
    {
        if (!_building)
        {
            throw new InvalidOperationException("Call Begin first.");
        }

        _back.Validate();
        DisplayList.ComputeDamage(_front, _back, Damage);
        (_front, _back) = (_back, _front);
        _building = false;
    }

    /// <summary>Forgets the accumulated damage once the viewer has redrawn it.</summary>
    public void ClearDamage() => Damage.Clear();

    /// <summary>Patches a transform of the current list in place and records the damage; see <see cref="DisplayList.PatchTransform"/>.</summary>
    /// <param name="id">The id of the transform item.</param>
    /// <param name="matrix">The new matrix.</param>
    public void PatchTransform(ItemId id, in Matrix3x2 matrix) => _front.PatchTransform(id, matrix, Damage);

    /// <summary>Patches a group opacity of the current list in place and records the damage; see <see cref="DisplayList.PatchOpacity"/>.</summary>
    /// <param name="id">The id of the group item.</param>
    /// <param name="opacity">The new opacity, 0 to 1.</param>
    public void PatchOpacity(ItemId id, float opacity) => _front.PatchOpacity(id, opacity, Damage);

    /// <summary>
    /// Tells the list that the host replaced the pixels behind an image handle and kept their size. Every item of the current list that
    /// draws the handle (plain, tiled, the pieces of a nine-slice) adds its bounds to <see cref="Damage"/>. Nothing is laid out or painted
    /// again, no item changes, and the call allocates nothing; the cost is one pass over the items.
    /// </summary>
    /// <param name="image">The handle whose pixels changed.</param>
    /// <returns>The number of items that draw the handle; 0 means nothing needs redrawing.</returns>
    public int InvalidateImage(ImageHandle image) => _front.InvalidateImage(image, Damage);
}
