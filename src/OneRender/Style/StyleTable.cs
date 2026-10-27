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

/// <summary>
/// The retained store of distinct computed styles. Nodes hold a <see cref="StyleId"/>; two nodes whose declarations and
/// parent style are the same share one row, so the cost of a style is paid once per distinct style, not once per node.
/// Rows are kept between rebuilds, which is what lets a second build of the same document allocate nothing.
/// </summary>
public sealed class StyleTable
{
    /// <summary>The most distinct styles one table holds. Further requests reuse the parent's row and raise a diagnostic.</summary>
    public const int MaxStyles = 1 << 16;

    private readonly StyleComputer _computer = new();
    private readonly Dictionary<StyleKey, int> _intern = new(256);
    private readonly Dictionary<long, int> _anonymous = new(16);
    private readonly Dictionary<int, int> _byHash = new(256);
    private int[] _sameHash = new int[64];
    private ComputedValues[] _rows = new ComputedValues[64];
    private int _count = 1; // row 0 is the "none" row (initial values), so a default StyleId is harmless.
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;
    private double _rootFontSize = 16;

    /// <summary>Creates a table whose row 0 is the initial style.</summary>
    public StyleTable() => _rows[0] = ComputedValues.Initial;

    /// <summary>The number of rows, including the initial-values row 0.</summary>
    public int Count => _count;

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

    /// <summary>Drops every cached style. Handles obtained before the call must not be used afterwards.</summary>
    public void Reset()
    {
        _intern.Clear();
        _anonymous.Clear();
        _byHash.Clear();
        _count = 1;
        _rootFontSize = 16;
    }

    /// <summary>The text service whose metrics resolve <c>ex</c>, <c>ch</c> and <c>ic</c>; set before building, since cached styles keep the numbers they were computed with.</summary>
    internal Layout.IFontProvider? Fonts
    {
        get => _computer.Fonts;
        set => _computer.Fonts = value;
    }

    /// <summary>Records which style is the root's, so <c>rem</c> units in later styles resolve against its font size.</summary>
    internal void NoteRoot(StyleId root) => _rootFontSize = _rows[root.Index].FontSize;

    internal bool TryGetCached(object source, int version, StyleId parent, out StyleId id)
    {
        if (_intern.TryGetValue(new StyleKey(source, version, parent.Index, _rootFontSize), out int row))
        {
            id = new StyleId(row);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Computes the style of a node and caches it under the key and version.</summary>
    internal StyleId Compute(object key, int version, DeclarationSource declarations, StyleId parent, bool hasParent, bool isRoot)
    {
        var env = new ComputeEnvironment(_viewportWidth, _viewportHeight, isRoot ? 16 : _rootFontSize);
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
        }

        if (!TryAdd(in values, out StyleId id))
        {
            Diagnostics.Add(DiagnosticKind.LimitExceeded, "style table full; reusing the parent's style");
            return hasParent ? parent : default;
        }

        _intern[new StyleKey(key, version, parent.Index, _rootFontSize)] = id.Index;
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

    // ---- rows owned by one node (animation) -------------------------------------------------------------------------------------

    private int[] _freePrivate = new int[8];
    private int _freePrivateCount;

    /// <summary>
    /// Makes a row that belongs to one node and is changed in place by <see cref="MutableRow"/> (an animated element's current style).
    /// It is not shared and never found by value; release it with <see cref="ReleasePrivate"/>. Counts toward <see cref="MaxStyles"/>.
    /// </summary>
    /// <returns>False when the table is full.</returns>
    internal bool TryAllocatePrivate(in ComputedValues values, out StyleId id)
    {
        if (_freePrivateCount > 0)
        {
            int reused = _freePrivate[--_freePrivateCount];
            _rows[reused] = values;
            id = new StyleId(reused);
            return true;
        }

        if (_count >= MaxStyles)
        {
            id = default;
            return false;
        }

        if (_count == _rows.Length)
        {
            Array.Resize(ref _rows, _rows.Length * 2);
            Array.Resize(ref _sameHash, _rows.Length);
        }

        _rows[_count] = values;
        _sameHash[_count] = 0;
        id = new StyleId(_count++);
        return true;
    }

    /// <summary>Gives a row from <see cref="TryAllocatePrivate"/> back for reuse. The caller must not use the id afterwards.</summary>
    internal void ReleasePrivate(StyleId id)
    {
        if (id.Index <= 0)
        {
            return;
        }

        if (_freePrivateCount == _freePrivate.Length)
        {
            Array.Resize(ref _freePrivate, _freePrivate.Length * 2);
        }

        _freePrivate[_freePrivateCount++] = id.Index;
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
        var env = new ComputeEnvironment(_viewportWidth, _viewportHeight, _rootFontSize);
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
                    id = new StyleId(row);
                    return true;
                }
            }
        }

        if (_count >= MaxStyles)
        {
            id = default;
            return false;
        }

        if (_count == _rows.Length)
        {
            Array.Resize(ref _rows, _rows.Length * 2);
            Array.Resize(ref _sameHash, _rows.Length);
        }

        _rows[_count] = values;
        _sameHash[_count] = _byHash.TryGetValue(hash, out int head) ? head : 0;
        _byHash[hash] = _count;
        id = new StyleId(_count++);
        return true;
    }

    private readonly record struct StyleKey(object Source, int Version, int Parent, double RootFontSize);
}
