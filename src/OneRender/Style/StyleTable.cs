using System.Runtime.CompilerServices;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>A handle to one row of a <see cref="StyleTable"/>: the computed values shared by every node with the same style.</summary>
public readonly struct StyleId : IEquatable<StyleId>
{
    internal StyleId(int index) => Index = index;

    /// <summary>The row number inside the owning <see cref="StyleTable"/>.</summary>
    public int Index { get; }

    /// <summary>True for the default value, which refers to no row.</summary>
    public bool IsNone => Index <= 0;

    /// <inheritdoc />
    public bool Equals(StyleId other) => Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StyleId s && Equals(s);

    /// <inheritdoc />
    public override int GetHashCode() => Index;

    /// <summary>Equality.</summary>
    public static bool operator ==(StyleId left, StyleId right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(StyleId left, StyleId right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => "style#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>An object that keeps <see cref="StyleId"/>s and reports them to <see cref="StyleTable.Reclaim"/> so their rows survive.</summary>
internal interface IStyleHolder
{
    /// <summary>Calls <see cref="StyleTable.Mark"/> for every row the holder will still read.</summary>
    void MarkStyles(StyleTable table);
}

/// <summary>
/// The retained store of distinct computed styles. Nodes hold a <see cref="StyleId"/>; two nodes whose declarations and
/// parent style are the same share one row, so the cost of a style is paid once per distinct style, not once per node.
/// Rows are kept between rebuilds, which is what lets a second build of the same document allocate nothing.
/// </summary>
public sealed class StyleTable
{
    /// <summary>The most distinct styles one table holds. Further requests reuse the parent's row and raise a diagnostic.</summary>
    public const int MaxStyles = 1 << 16;

    private const int MinReclaimGap = 1024;

    /// <summary>Counts the resets of any table; caches keyed by style row drop their entries when it changes.</summary>
    internal static int ResetGeneration { get; private set; }

    private readonly StyleComputer _computer = new();
    private readonly Dictionary<StyleKey, int> _intern = new(256);
    private readonly Dictionary<long, int> _anonymous = new(16);
    private readonly Dictionary<int, int> _byHash = new(256);

    // Styles computed from builder declarations are remembered on the declaration (weakly), not in _intern: a game that makes a
    // declaration per node it inserts must not keep every declaration it ever made alive. Declarations a host keeps and shares keep
    // their entries for as long as they live, so reusing them computes nothing.
    private readonly ConditionalWeakTable<object, SourceCache> _bySource = new();
    private int[] _sameHash = new int[64];
    private ComputedValues[] _rows = new ComputedValues[64];

    // Row reclamation (see Reclaim). _gen counts how often a row was freed, so a weakly cached entry that names a freed row (or a freed
    // parent row) is recognised and ignored without scanning the weak table.
    private int[] _gen = new int[64];
    private int[] _hash = new int[64];
    private bool[] _private = new bool[64];
    private bool[] _mark = new bool[64];
    private int[] _used = new int[64];
    private int _epoch = 1;
    private int[] _free = new int[64];
    private int _freeCount;
    private int _reclaimAt = MinReclaimGap;
    private int _reclaims;
    private readonly List<WeakReference<IStyleHolder>> _holders = new(4);
    private int _count = 1; // row 0 is the "none" row (initial values), so a default StyleId is harmless.
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;
    private double _rootFontSize = 16;
    private bool _prefersDark;
    private ComputeEnvironment _rootEnv = new(0, 0, 16);

    /// <summary>Creates a table whose row 0 is the initial style.</summary>
    public StyleTable() => _rows[0] = ComputedValues.Initial;

    /// <summary>The number of row slots in use or waiting for reuse, including the initial-values row 0; it never exceeds <see cref="MaxStyles"/>.</summary>
    public int Count => _count;

    /// <summary>The number of rows that hold a style (<see cref="Count"/> minus the slots waiting for reuse), including row 0.</summary>
    public int LiveCount => _count - _freeCount;

    /// <summary>How many times <see cref="Reclaim"/> freed rows.</summary>
    public int ReclaimCount => _reclaims;

    /// <summary>Problems met while computing styles; cleared by <see cref="Tree.StyledTree.Clear"/>.</summary>
    public DiagnosticBag Diagnostics { get; } = new();

    /// <summary>The viewport width in px that <c>vw</c> units compute against. Default 800.</summary>
    public double ViewportWidth => _viewportWidth;

    /// <summary>The viewport height in px that <c>vh</c> units compute against. Default 600.</summary>
    public double ViewportHeight => _viewportHeight;

    /// <summary>The computed values of a style.</summary>
    /// <param name="id">A handle from this table.</param>
    public ref readonly ComputedValues this[StyleId id] => ref _rows[id.Index];

    /// <summary>
    /// Sets the viewport that viewport-relative units compute against. A change drops every cached style (their
    /// pixel values are stale), so call it before building a tree, not between builds of the same tree.
    /// </summary>
    /// <param name="width">Width in px; at least 0. NaN counts as 0.</param>
    /// <param name="height">Height in px; at least 0. NaN counts as 0.</param>
    public void SetViewport(double width, double height)
    {
        width = double.IsNaN(width) ? 0 : Math.Max(0, width);
        height = double.IsNaN(height) ? 0 : Math.Max(0, height);
        if (width == _viewportWidth && height == _viewportHeight)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        Reset();
    }

    /// <summary>
    /// Whether the host prefers a dark color scheme (the value of the <c>prefers-color-scheme</c> media feature). A <c>color-scheme</c> that lists both
    /// <c>light</c> and <c>dark</c> resolves to it (CSS Color Adjust 1 §2.2). Default false. A change drops every cached style, so set it before building a tree.
    /// </summary>
    public bool PrefersDarkScheme
    {
        get => _prefersDark;
        set
        {
            if (_prefersDark == value)
            {
                return;
            }

            _prefersDark = value;
            Reset();
        }
    }

    /// <summary>Drops every cached style. Handles obtained before the call must not be used afterwards.</summary>
    public void Reset()
    {
        ResetGeneration++;
        _intern.Clear();
        _bySource.Clear();
        _anonymous.Clear();
        _byHash.Clear();
        _count = 1;
        _freeCount = 0;
        _reclaimAt = MinReclaimGap;
        Array.Clear(_gen);
        _rootFontSize = 16;
        _rootEnv = new(0, 0, 16, PrefersDark: _prefersDark);
    }

    /// <summary>The text service whose metrics resolve <c>ex</c>, <c>ch</c> and <c>ic</c>; set before building, since cached styles keep the numbers they were computed with.</summary>
    internal Layout.IFontProvider? Fonts
    {
        get => _computer.Fonts;
        set => _computer.Fonts = value;
    }

    /// <summary>Records which style is the root's, so <c>rem</c> units in later styles resolve against its font size.</summary>
    /// <summary>The font size that <c>rem</c> units currently resolve against.</summary>
    internal double RootFontSizeValue => _rootFontSize;

    /// <summary>The root font facts that <c>rem</c>, <c>rex</c>, <c>rcap</c>, <c>rch</c> and <c>ric</c> resolve against; it changes with the root's font size, family, weight or style.</summary>
    internal ComputeEnvironment RootEnvironment => _rootEnv;

    internal void NoteRoot(StyleId root)
    {
        _rootFontSize = _rows[root.Index].FontSize;
        _rootEnv = _computer.WithRootFont(_rootEnv, in _rows[root.Index]);
    }

    private sealed class SourceCache
    {
        public const int MaxEntries = 32;
        public Entry[] Entries = new Entry[2];
        public int Count;
        public int Next;
    }

    private struct Entry
    {
        public int Version;
        public int Parent;
        public int ParentGen;
        public int Row;
        public int RowGen;
        public ComputeEnvironment Root;
    }

    /// <summary>The style remembered for a builder declaration (or layer key) under a parent style, if any.</summary>
    internal bool TryGetCachedWeak(object source, int version, StyleId parent, out StyleId id)
    {
        if (_bySource.TryGetValue(source, out SourceCache? cache))
        {
            Entry[] entries = cache.Entries;
            for (int i = 0; i < cache.Count; i++)
            {
                ref Entry e = ref entries[i];
                if (e.Version == version && e.Parent == parent.Index && e.Root == _rootEnv && e.ParentGen == _gen[e.Parent] && e.RowGen == _gen[e.Row])
                {
                    id = new StyleId(e.Row);
                    _used[e.Row] = _epoch;
                    return true;
                }
            }
        }

        id = default;
        return false;
    }

    private void RememberWeak(object source, int version, int parent, int row)
    {
        if (!_bySource.TryGetValue(source, out SourceCache? cache))
        {
            cache = new SourceCache();
            _bySource.Add(source, cache);
        }

        // Entries of an older version of the declaration can never be asked for again; they go first.
        int slot = -1;
        for (int i = 0; i < cache.Count; i++)
        {
            ref Entry old = ref cache.Entries[i];
            if (old.Version != version || old.ParentGen != _gen[old.Parent] || old.RowGen != _gen[old.Row])
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            if (cache.Count < SourceCache.MaxEntries)
            {
                if (cache.Count == cache.Entries.Length)
                {
                    Array.Resize(ref cache.Entries, Math.Min(SourceCache.MaxEntries, cache.Entries.Length * 2));
                }

                slot = cache.Count++;
            }
            else
            {
                slot = cache.Next;
                cache.Next = (cache.Next + 1) % SourceCache.MaxEntries;
            }
        }

        cache.Entries[slot] = new Entry { Version = version, Parent = parent, ParentGen = _gen[parent], Row = row, RowGen = _gen[row], Root = _rootEnv };
    }

    internal bool TryGetCached(object source, int version, StyleId parent, out StyleId id)
    {
        if (_intern.TryGetValue(new StyleKey(source, version, parent.Index, _rootEnv), out int row))
        {
            _used[row] = _epoch;
            id = new StyleId(row);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Computes the style of a node and caches it under the key and version.</summary>
    internal StyleId Compute(object key, int version, DeclarationSource declarations, StyleId parent, bool hasParent, bool isRoot, bool weak = false)
    {
        ComputeEnvironment env = isRoot
            ? new ComputeEnvironment(_viewportWidth, _viewportHeight, 16, PrefersDark: _prefersDark)
            : _rootEnv with { ViewportWidth = _viewportWidth, ViewportHeight = _viewportHeight, RootFontSize = _rootFontSize, PrefersDark = _prefersDark };
        ref ComputedValues parentRow = ref _rows[hasParent ? parent.Index : 0];
        ComputedValues values = _computer.Compute(declarations, in parentRow, hasParent, in env, isRoot);
        if (_computer.InvalidCount > 0)
        {
            Diagnostics.Add(DiagnosticKind.InvalidValue, _computer.InvalidCount == 1
                ? "ignored an invalid value for " + _computer.FirstInvalid
                : "ignored " + _computer.InvalidCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " invalid values, first for " + _computer.FirstInvalid);
        }

        if (isRoot)
        {
            _rootFontSize = values.FontSize;
            _rootEnv = _computer.WithRootFont(_rootEnv, in values);
        }

        if (!TryAdd(in values, out StyleId id))
        {
            Diagnostics.Add(DiagnosticKind.LimitExceeded, "style table full; reusing the parent's style");
            return hasParent ? parent : default;
        }

        if (weak)
        {
            RememberWeak(key, version, parent.Index, id.Index);
        }
        else
        {
            _intern[new StyleKey(key, version, parent.Index, _rootEnv)] = id.Index;
        }

        return id;
    }

    /// <summary>A copy of a style with some values changed (presentational hints), shared with any equal style.</summary>
    internal StyleId Derive(StyleId id, in ComputedValues changed) => TryAdd(in changed, out StyleId derived) ? derived : id;

    /// <summary>The style of an anonymous box under <paramref name="parent"/> (CSS2 §9.2.1.1): inherited values, everything else initial.</summary>
    /// <param name="parent">The style of the box's parent.</param>
    /// <param name="display">The display of the anonymous box (block or inline flow).</param>
    public StyleId GetAnonymous(StyleId parent, DisplayType display)
    {
        long key = ((long)parent.Index << 8) | ((long)(byte)display.Outside << 4) | (long)(byte)display.Inside;
        if (_anonymous.TryGetValue(key, out int row))
        {
            _used[row] = _epoch;
            return new StyleId(row);
        }

        ComputedValues v = StyleComputer.Anonymous(in _rows[parent.Index], display);
        if (!TryAdd(in v, out StyleId id))
        {
            Diagnostics.Add(DiagnosticKind.LimitExceeded, "style table full; reusing the parent's style for an anonymous box");
            return parent;
        }

        _anonymous[key] = id.Index;
        return id;
    }

    // ---- reclamation -----------------------------------------------------------------------------------------------------------
    //
    // Rows are shared by value, so no row has one owner to free it. Instead the table sweeps: every object that keeps StyleIds registers
    // as an IStyleHolder (styled trees, box trees, the animation controller), and Reclaim asks each of them to Mark the rows it still
    // reads. Unmarked rows go to a free list and are reused, the value indexes and the caches keyed by a row are purged of entries that
    // name a freed row, and the generation of a freed row changes so weakly cached entries that name it stop matching.
    //
    // Sweeps run only at points where no StyleId is held in a local: the end of a flush. A StyleId obtained from a node or box is valid
    // until the next flush; a holder outside the engine that keeps one longer must not rely on it.

    /// <summary>Registers an object whose StyleIds must survive <see cref="Reclaim"/>. The table keeps only a weak reference.</summary>
    internal void AddHolder(IStyleHolder holder) => _holders.Add(new WeakReference<IStyleHolder>(holder));

    /// <summary>Marks a row as still in use during <see cref="Reclaim"/>. Row 0 and out-of-range ids are ignored.</summary>
    internal void Mark(int index)
    {
        if ((uint)index < (uint)_count)
        {
            _mark[index] = true;
        }
    }

    /// <summary>Runs <see cref="Reclaim"/> when the rows in use have grown well past what the last sweep left. Cheap to call after every flush.</summary>
    internal void ReclaimIfNeeded()
    {
        if (_count - _freeCount > _reclaimAt)
        {
            Reclaim();
        }
    }

    /// <summary>
    /// Frees every row that no registered holder refers to, so content that keeps producing new style values (a position set from the
    /// host every frame, a counter's width) reuses rows instead of filling the table. Allocates nothing once the arrays have grown.
    /// </summary>
    /// <remarks>Call it only when no <see cref="StyleId"/> is held in a local variable, for example right after a flush. Live elements keep their values.</remarks>
    /// <returns>The number of rows freed.</returns>
    public int Reclaim()
    {
        int before = _count - _freeCount;
        Array.Clear(_mark, 0, _count);
        _mark[0] = true;
        for (int i = 0; i < _holders.Count;)
        {
            if (_holders[i].TryGetTarget(out IStyleHolder? holder))
            {
                holder.MarkStyles(this);
                i++;
            }
            else
            {
                int last = _holders.Count - 1;
                _holders[i] = _holders[last];
                _holders.RemoveAt(last);
            }
        }

        int held = 1;
        for (int i = 1; i < _count; i++)
        {
            if (_mark[i])
            {
                held++;
            }
        }

        // Rows no holder reads stay for one more interval when they were looked up or made since the last sweep: a style a host asks for
        // again and again (cached by the declaration that produced it) is then never computed twice, while a style that was used once and
        // dropped (a position that took a new value) goes at the next sweep. Marked rows always stay.
        for (int i = 1; i < _count; i++)
        {
            if (_used[i] == _epoch)
            {
                _mark[i] = true;
            }
        }

        _epoch++;

        // Entries that name a freed row, as their value or as the parent they were computed under, can never be right again.
        // (Removing from a Dictionary while enumerating it is allowed and allocates nothing.)
        foreach (KeyValuePair<StyleKey, int> kv in _intern)
        {
            if (!_mark[kv.Value] || !_mark[kv.Key.Parent])
            {
                _intern.Remove(kv.Key);
            }
        }

        foreach (KeyValuePair<long, int> kv in _anonymous)
        {
            if (!_mark[kv.Value] || !_mark[(int)(kv.Key >> 8)])
            {
                _anonymous.Remove(kv.Key);
            }
        }

        int newCount = 1;
        for (int i = _count - 1; i >= 1; i--)
        {
            if (_mark[i])
            {
                newCount = i + 1;
                break;
            }
        }

        _freeCount = 0;
        for (int i = _count - 1; i >= 1; i--)
        {
            if (_mark[i])
            {
                continue;
            }

            _gen[i]++;
            _private[i] = false;
            if (i < newCount)
            {
                PushFree(i);
            }
        }

        _count = newCount;
        _byHash.Clear();
        for (int i = 1; i < newCount; i++)
        {
            if (_mark[i] && !_private[i])
            {
                int h = _hash[i];
                _sameHash[i] = _byHash.TryGetValue(h, out int head) ? head : 0;
                _byHash[h] = i;
            }
        }

        int live = _count - _freeCount;
        // The next sweep is due when as many rows again as the rows in use have been made: at least MinReclaimGap, and a share of what
        // the holders keep so a big document is not swept every few frames. The gap does not grow with the rows kept for one interval.
        _reclaimAt = Math.Min(live + Math.Max(MinReclaimGap, Math.Min(held, 16384)), MaxStyles - 1);
        int freed = before - live;
        if (freed > 0)
        {
            ResetGeneration++;
            _reclaims++;
        }

        return freed;
    }

    // ---- rows owned by one node (animation) -------------------------------------------------------------------------------------

    /// <summary>
    /// Makes a row that belongs to one node and is changed in place by <see cref="MutableRow"/> (an animated element's current style).
    /// It is not shared and never found by value; release it with <see cref="ReleasePrivate"/>. Counts toward <see cref="MaxStyles"/>.
    /// </summary>
    /// <returns>False when the table is full.</returns>
    internal bool TryAllocatePrivate(in ComputedValues values, out StyleId id)
    {
        int row = TakeSlot();
        if (row < 0)
        {
            id = default;
            return false;
        }

        _rows[row] = values;
        _sameHash[row] = 0;
        _private[row] = true;
        id = new StyleId(row);
        return true;
    }

    /// <summary>Gives a row from <see cref="TryAllocatePrivate"/> back for reuse. The caller must not use the id afterwards.</summary>
    internal void ReleasePrivate(StyleId id)
    {
        if (id.Index <= 0 || id.Index >= _count || !_private[id.Index])
        {
            return;
        }

        _private[id.Index] = false;
        _gen[id.Index]++;
        PushFree(id.Index); // the values stay: the node's old style may still be read until the restyle that follows is done
    }

    // A slot for a new row: a freed one when there is any, otherwise the next one. -1 when the table is full.
    private int TakeSlot()
    {
        if (_freeCount > 0)
        {
            int reused = _free[--_freeCount];
            _used[reused] = _epoch;
            return reused;
        }

        if (_count >= MaxStyles)
        {
            return -1;
        }

        if (_count == _rows.Length)
        {
            int n = _rows.Length * 2;
            Array.Resize(ref _rows, n);
            Array.Resize(ref _sameHash, n);
            Array.Resize(ref _gen, n);
            Array.Resize(ref _hash, n);
            Array.Resize(ref _private, n);
            Array.Resize(ref _mark, n);
            Array.Resize(ref _used, n);
        }

        _used[_count] = _epoch;
        return _count++;
    }

    private void PushFree(int row)
    {
        if (_freeCount == _free.Length)
        {
            Array.Resize(ref _free, _free.Length * 2);
        }

        _free[_freeCount++] = row;
    }

    /// <summary>The row of a private style, for in-place change. Only valid for ids from <see cref="TryAllocatePrivate"/>.</summary>
    internal ref ComputedValues MutableRow(StyleId id) => ref _rows[id.Index];

    /// <summary>Copies one property from <paramref name="source"/> into <paramref name="target"/>, all the fields that make it up.</summary>
    internal void CopyProperty(PropertyId id, ref ComputedValues target, in ComputedValues source) => _computer.CopyProperty(id, ref target, in source, false);

    /// <summary>
    /// Computes a style from declarations without storing it: the values a declaration block would give an element whose parent has
    /// <paramref name="parent"/>. Used for keyframe values, which are never nodes' styles.
    /// </summary>
    internal ComputedValues ComputeDetached(DeclarationSource declarations, StyleId parent, bool hasParent)
    {
        var env = new ComputeEnvironment(_viewportWidth, _viewportHeight, _rootFontSize, PrefersDark: _prefersDark);
        return _computer.Compute(declarations, in _rows[hasParent ? parent.Index : 0], hasParent, in env, false);
    }

    /// <summary>Adds a row, or returns the existing row with exactly these values, so equal styles share one row however they were reached.</summary>
    private bool TryAdd(in ComputedValues values, out StyleId id)
    {
        int hash = ComputedValues.ValueHash(in values);
        if (_byHash.TryGetValue(hash, out int row))
        {
            for (; row > 0; row = _sameHash[row])
            {
                if (ComputedValues.ValueEquals(in _rows[row], in values))
                {
                    _used[row] = _epoch;
                    id = new StyleId(row);
                    return true;
                }
            }
        }

        int slot = TakeSlot();
        if (slot < 0)
        {
            id = default;
            return false;
        }

        _rows[slot] = values;
        _hash[slot] = hash;
        _private[slot] = false;
        _sameHash[slot] = _byHash.TryGetValue(hash, out int head) ? head : 0;
        _byHash[hash] = slot;
        id = new StyleId(slot);
        return true;
    }

    private readonly record struct StyleKey(object Source, int Version, int Parent, ComputeEnvironment Root);
}
