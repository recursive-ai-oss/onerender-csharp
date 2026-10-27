namespace OneRender.Layout;

/// <summary>
/// What a layout changed compared with the layout before it, when both produced fragments in the same places: the fragments that
/// layout wrote again (not copied) and whose values differ from the earlier ones. Paint rewrites only these.
/// </summary>
internal sealed class LayoutDelta
{
    /// <summary>The fragment is itself different (size, text, edges ...).</summary>
    internal const byte Changed = 1;

    /// <summary>The fragment moved, or its baseline changed: everything below it is painted somewhere else.</summary>
    internal const byte Moved = 2;

    /// <summary>Only the scrollable overflow differs; paint cares only for boxes that scroll or clip.</summary>
    internal const byte OverflowOnly = 4;

    private int[] _index = new int[64];
    private byte[] _flags = new byte[64];

    /// <summary>True when the fragments of both layouts correspond one to one (same count, same kind, box and parent at every written index).</summary>
    internal bool Aligned { get; private set; }

    /// <summary>The number of changed fragments.</summary>
    internal int Count { get; private set; }

    internal int IndexAt(int i) => _index[i];

    internal byte FlagsAt(int i) => _flags[i];

    internal void Reset(bool aligned)
    {
        Aligned = aligned;
        Count = 0;
    }

    internal void Add(int index, byte flags)
    {
        if (Count == _index.Length)
        {
            Array.Resize(ref _index, Count * 2);
            Array.Resize(ref _flags, Count * 2);
        }

        _index[Count] = index;
        _flags[Count] = flags;
        Count++;
    }

    internal void SetUnaligned() => Aligned = false;
}

public sealed partial class FragmentTree
{
    // Where a fragment was copied from in the tree layout reused, or -1 when layout wrote it.
    private int[] _srcOf = new int[64];
    private bool _copyShifted;

    /// <summary>
    /// Compares this tree (just laid out, not yet published) with <paramref name="old"/> (the published one) and fills
    /// <paramref name="delta"/>: aligned or not, and which written fragments differ.
    /// </summary>
    internal void DiffAgainst(FragmentTree old, LayoutDelta delta)
    {
        if (_count != old._count || _copyShifted || !ReferenceEquals(Boxes, old.Boxes) || _tbCount != 0 || old._tbCount != 0)
        {
            delta.Reset(false);
            return;
        }

        delta.Reset(true);
        for (int i = 0; i < _count; i++)
        {
            if (_srcOf[i] >= 0)
            {
                continue;
            }

            if (_kind[i] != old._kind[i] || _box[i] != old._box[i] || _parent[i] != old._parent[i])
            {
                delta.SetUnaligned();
                return;
            }

            byte flags = 0;
            if (_x[i] != old._x[i] || _y[i] != old._y[i] || BitsDiffer(_baseline[i], old._baseline[i]))
            {
                flags = LayoutDelta.Changed | LayoutDelta.Moved;
            }
            else if (!SameValues(old, i, out bool overflowOnly))
            {
                flags = overflowOnly ? LayoutDelta.OverflowOnly : LayoutDelta.Changed;
            }

            if (flags != 0)
            {
                delta.Add(i, flags);
            }
        }
    }

    internal bool HasTableBorders => _tbCount > 0;

    private static bool BitsDiffer(double a, double b) => BitConverter.DoubleToInt64Bits(a) != BitConverter.DoubleToInt64Bits(b);

    private bool SameValues(FragmentTree o, int i, out bool overflowOnly)
    {
        overflowOnly = false;
        if (_w[i] != o._w[i] || _h[i] != o._h[i] || _traits[i] != o._traits[i] || _fragmentainer[i] != o._fragmentainer[i]
            || _textStart[i] != o._textStart[i] || _textLength[i] != o._textLength[i]
            || !_border[i].Equals(o._border[i]) || !_padding[i].Equals(o._padding[i]))
        {
            return false;
        }

        if (!SameRun(o, i))
        {
            return false;
        }

        if (!_overflow[i].Equals(o._overflow[i]))
        {
            overflowOnly = true;
            return false;
        }

        return true;
    }

    private bool SameRun(FragmentTree o, int i)
    {
        int ra = _run[i], rb = o._run[i];
        if ((ra < 0) != (rb < 0))
        {
            return false;
        }

        if (ra < 0)
        {
            return true;
        }

        GlyphRun a = _runs[ra], b = o._runs[rb];
        return ReferenceEquals(a.Face, b.Face) && a.FontSize == b.FontSize && a.TextStart == b.TextStart && a.TextLength == b.TextLength
            && a.RightToLeft == b.RightToLeft && a.GlyphCount == b.GlyphCount
            && new ReadOnlySpan<int>(_glyphId, a.GlyphStart, a.GlyphCount).SequenceEqual(new ReadOnlySpan<int>(o._glyphId, b.GlyphStart, b.GlyphCount))
            && new ReadOnlySpan<double>(_glyphX, a.GlyphStart, a.GlyphCount).SequenceEqual(new ReadOnlySpan<double>(o._glyphX, b.GlyphStart, b.GlyphCount))
            && new ReadOnlySpan<double>(_glyphY, a.GlyphStart, a.GlyphCount).SequenceEqual(new ReadOnlySpan<double>(o._glyphY, b.GlyphStart, b.GlyphCount))
            && new ReadOnlySpan<int>(_glyphCluster, a.GlyphStart, a.GlyphCount).SequenceEqual(new ReadOnlySpan<int>(o._glyphCluster, b.GlyphStart, b.GlyphCount));
    }
}
