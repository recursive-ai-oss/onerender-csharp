using OneRender.Style;

namespace OneRender.Layout;

/// <summary>The border styles and colors a box paints in the collapsing border model (CSS2 §17.6.2): the winners of the conflict resolution on each side.</summary>
internal struct TableBorderPaint
{
    public BorderStyleKind StyleTop, StyleRight, StyleBottom, StyleLeft;
    public ColorValue ColorTop, ColorRight, ColorBottom, ColorLeft;
}

public sealed partial class FragmentTree
{
    private int[] _tbIndex = [];
    private TableBorderPaint[] _tb = new TableBorderPaint[4];
    private int _tbCount;

    private void ClearTableBorders()
    {
        if (_tbCount > 0)
        {
            Array.Fill(_tbIndex, -1);
            _tbCount = 0;
        }
    }

    /// <summary>Records the collapsed-border winners that fragment <paramref name="fragment"/> paints instead of its own style's borders.</summary>
    internal void SetTableBorders(int fragment, in TableBorderPaint paint)
    {
        if (fragment >= _tbIndex.Length)
        {
            int old = _tbIndex.Length;
            Array.Resize(ref _tbIndex, Math.Max(fragment + 1, Math.Max(64, old * 2)));
            Array.Fill(_tbIndex, -1, old, _tbIndex.Length - old);
        }

        int slot = _tbIndex[fragment];
        if (slot < 0)
        {
            if (_tbCount == _tb.Length)
            {
                Array.Resize(ref _tb, _tbCount * 2);
            }

            slot = _tbCount++;
            _tbIndex[fragment] = slot;
        }

        _tb[slot] = paint;
    }

    internal bool TryGetTableBorders(int fragment, out TableBorderPaint paint)
    {
        if (_tbCount > 0 && fragment < _tbIndex.Length && _tbIndex[fragment] >= 0)
        {
            paint = _tb[_tbIndex[fragment]];
            return true;
        }

        paint = default;
        return false;
    }
}
