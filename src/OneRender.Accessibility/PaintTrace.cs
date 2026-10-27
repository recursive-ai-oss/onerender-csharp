using OneRender.Layout;
using OneRender.Painting;

namespace OneRender.Accessibility;

/// <summary>
/// Records, while a <see cref="Painter"/> paints, which paint layer every fragment was painted in and where in the display
/// list its items start. Attach it with <c>painter.Observer = trace</c> before painting; painting the root fragment starts a
/// new trace, so one instance serves any number of paints. The facts builder reads it afterwards.
/// </summary>
/// <remarks>
/// A paint layer is a positioned box, a box that begins a stacking context (CSS 2.2 Appendix E) or a box that clips its content. Layers are numbered from 1
/// in the order they start painting, so a larger number paints later, and everything painted inside a layer without being a
/// layer itself shares its number. Not thread-safe.
/// </remarks>
public sealed class PaintTrace : IPaintObserver
{
    private int[] _layer = new int[64];
    private int[] _firstItem = new int[64];
    private byte[] _flags = new byte[64];
    private int[] _stack = new int[16];
    private int _depth;
    private int _counter;
    private int _fragments;

    private const byte Reported = 1;
    private const byte Stacking = 2;

    /// <summary>The number of fragments the traced tree had.</summary>
    public int FragmentCount => _fragments;

    /// <summary>The number of paint layers started by the last paint.</summary>
    public int LayerCount => _counter;

    /// <summary>Forgets the last paint. Called by the trace itself when the root fragment starts painting.</summary>
    /// <param name="fragmentCount">The number of fragments in the tree about to be painted.</param>
    public void Reset(int fragmentCount)
    {
        if (_layer.Length < fragmentCount)
        {
            int size = Math.Max(fragmentCount, _layer.Length * 2);
            _layer = new int[size];
            _firstItem = new int[size];
            _flags = new byte[size];
        }
        else
        {
            Array.Clear(_layer, 0, fragmentCount);
            Array.Clear(_firstItem, 0, fragmentCount);
            Array.Clear(_flags, 0, fragmentCount);
        }

        _fragments = fragmentCount;
        _depth = 0;
        _counter = 0;
    }

    /// <inheritdoc />
    public void EnterUnit(Fragment fragment, bool isLayer, bool isStackingContext, int firstItem)
    {
        if (fragment.Index == 0 && fragment.Tree is { } tree)
        {
            Reset(tree.Count);
        }

        int layer = isLayer || _depth == 0 ? ++_counter : _stack[_depth - 1];
        if (_depth == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_depth++] = layer;
        if ((uint)fragment.Index < (uint)_fragments)
        {
            _flags[fragment.Index] |= isStackingContext ? (byte)Stacking : (byte)0;
        }
    }

    /// <inheritdoc />
    public void ExitUnit(Fragment fragment, int endItem)
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }

    /// <inheritdoc />
    public void Content(Fragment fragment, int firstItem)
    {
        int i = fragment.Index;
        if (_depth == 0 || (uint)i >= (uint)_fragments)
        {
            return;
        }

        _layer[i] = _stack[_depth - 1];
        _firstItem[i] = firstItem;
        _flags[i] |= Reported;
    }

    /// <summary>The paint layer a fragment was painted in, or 0 when it was not painted by the last paint.</summary>
    /// <param name="fragment">A fragment of the traced tree.</param>
    /// <returns>The layer number, from 1.</returns>
    public int LayerOf(Fragment fragment) => LayerOf(fragment.Index);

    internal int LayerOf(int fragment) => (uint)fragment < (uint)_fragments && (_flags[fragment] & Reported) != 0 ? _layer[fragment] : 0;

    /// <summary>The display list index at which a fragment's own items start, or -1 when it was not painted.</summary>
    /// <param name="fragment">A fragment of the traced tree.</param>
    public int FirstItemOf(Fragment fragment) => FirstItemOf(fragment.Index);

    internal int FirstItemOf(int fragment) => (uint)fragment < (uint)_fragments && (_flags[fragment] & Reported) != 0 ? _firstItem[fragment] : -1;

    /// <summary>True when the fragment's box began a stacking context.</summary>
    /// <param name="fragment">A fragment of the traced tree.</param>
    public bool IsStackingContext(Fragment fragment) => IsStackingContext(fragment.Index);

    internal bool IsStackingContext(int fragment) => (uint)fragment < (uint)_fragments && (_flags[fragment] & Stacking) != 0;
}
