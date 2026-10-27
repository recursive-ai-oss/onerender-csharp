using System.Text;
using OneRender.Tree;
using OneFont;
using RecursiveAI.OneHtml;

namespace OneRender.Layout;

/// <summary>
/// The fragment tree: what layout writes and paint, hit testing and tooling read. Each fragment is a piece of a box (or a
/// line, or a run of text) with its border box, resolved border and padding, scrollable overflow and, for text, its shaped
/// glyphs. Struct-of-arrays columns with <see cref="Fragment"/> handles; the engine appends fragments in tree order.
/// </summary>
/// <remarks>
/// <para>Coordinates: a fragment's offset is relative to the top-left of its parent fragment's border box. The root's offset
/// is relative to the origin of the document. Out-of-flow boxes are children of the fragment of their containing block.</para>
/// <para>The tree is retained: <see cref="Clear"/> keeps the storage, so laying out the same boxes again allocates nothing once warm.
/// Not thread-safe; one layout writes at a time.</para>
/// </remarks>
public sealed class FragmentTree
{
    private byte[] _kind = new byte[64];
    private byte[] _traits = new byte[64];
    private int[] _box = new int[64];
    private int[] _parent = new int[64];
    private int[] _first = new int[64];
    private int[] _last = new int[64];
    private int[] _next = new int[64];
    private int[] _nextOfBox = new int[64];
    private int[] _textStart = new int[64];
    private int[] _textLength = new int[64];
    private int[] _run = new int[64];
    private double[] _x = new double[64];
    private double[] _y = new double[64];
    private double[] _w = new double[64];
    private double[] _h = new double[64];
    private double[] _baseline = new double[64];
    private Insets[] _border = new Insets[64];
    private Insets[] _padding = new Insets[64];
    private Rect[] _overflow = new Rect[64];
    private int[] _boxFirst = [];
    private int[] _boxLast = [];
    private GlyphRun[] _runs = new GlyphRun[16];
    private int _runCount;
    private int[] _glyphId = new int[256];
    private double[] _glyphX = new double[256];
    private double[] _glyphY = new double[256];
    private int[] _glyphCluster = new int[256];
    private int _glyphCount;
    private int _count;

    /// <summary>Creates an empty fragment tree for the fragments of <paramref name="boxes"/>.</summary>
    /// <param name="boxes">The box tree whose boxes the fragments are pieces of.</param>
    public FragmentTree(BoxTree boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        Boxes = boxes;
    }

    /// <summary>The box tree the fragments belong to.</summary>
    public BoxTree Boxes { get; }

    /// <summary>The number of fragments.</summary>
    public int Count => _count;

    /// <summary>The number of glyph runs.</summary>
    public int GlyphRunCount => _runCount;

    /// <summary>The root fragment (the fragment of the root box), or the null handle when empty.</summary>
    public Fragment Root => _count == 0 ? default : new Fragment(this, 0);

    /// <summary>Removes every fragment and glyph run; keeps the storage. A layout calls this first.</summary>
    public void Clear()
    {
        _count = 0;
        _runCount = 0;
        _glyphCount = 0;
        int n = Boxes.Count;
        if (_boxFirst.Length < n)
        {
            int size = Math.Max(n, _boxFirst.Length * 2);
            _boxFirst = new int[size];
            _boxLast = new int[size];
        }

        Array.Fill(_boxFirst, -1);
        Array.Fill(_boxLast, -1);
    }

    // ---- writing (used by layout) ----

    /// <summary>Appends the fragment of a box as the last child of <paramref name="parent"/>.</summary>
    /// <param name="parent">The parent fragment; the null handle for the root (only one root is allowed).</param>
    /// <param name="box">The box this is a piece of.</param>
    /// <param name="borderBox">The border box relative to the parent's border-box origin.</param>
    /// <returns>The new fragment.</returns>
    /// <exception cref="ArgumentException"><paramref name="parent"/> is not from this tree, or a second root is added, or <paramref name="box"/> is not from this tree's box tree.</exception>
    public Fragment AddBox(Fragment parent, Box box, Rect borderBox)
    {
        if (box.IsNull || !ReferenceEquals(box.Tree, Boxes))
        {
            throw new ArgumentException("The box does not belong to this tree's box tree.", nameof(box));
        }

        int i = Append(FragmentKind.Box, parent, box.Index, borderBox);
        if (_boxFirst.Length <= box.Index)
        {
            int old = _boxFirst.Length;
            int size = Math.Max(Math.Max(Boxes.Count, box.Index + 1), old * 2);
            Array.Resize(ref _boxFirst, size);
            Array.Resize(ref _boxLast, size);
            Array.Fill(_boxFirst, -1, old, size - old);
            Array.Fill(_boxLast, -1, old, size - old);
        }

        if (_boxLast[box.Index] >= 0)
        {
            _nextOfBox[_boxLast[box.Index]] = i;
        }
        else
        {
            _boxFirst[box.Index] = i;
        }

        _boxLast[box.Index] = i;
        return new Fragment(this, i);
    }

    /// <summary>Appends a line box as the last child of <paramref name="parent"/>.</summary>
    /// <param name="parent">The fragment of the block container that establishes the inline formatting context.</param>
    /// <param name="lineBox">The line box rectangle relative to the parent's border-box origin.</param>
    /// <param name="baseline">The distance from the top of the line box to its baseline, in px.</param>
    /// <returns>The new fragment.</returns>
    public Fragment AddLine(Fragment parent, Rect lineBox, double baseline)
    {
        int i = Append(FragmentKind.Line, parent, -1, lineBox);
        _baseline[i] = baseline;
        return new Fragment(this, i);
    }

    /// <summary>Appends a run of text as the last child of <paramref name="parent"/>.</summary>
    /// <param name="parent">The line or inline-box fragment the text is on.</param>
    /// <param name="textBox">The text box the text belongs to.</param>
    /// <param name="textStart">First UTF-16 offset of the box's text shown here.</param>
    /// <param name="textLength">Number of UTF-16 units shown here.</param>
    /// <param name="borderBox">The rectangle the run occupies, relative to the parent's border-box origin.</param>
    /// <param name="run">The shaped glyphs, or <see cref="GlyphRunId.None"/>.</param>
    /// <returns>The new fragment.</returns>
    public Fragment AddText(Fragment parent, Box textBox, int textStart, int textLength, Rect borderBox, GlyphRunId run)
    {
        if (textBox.IsNull || !ReferenceEquals(textBox.Tree, Boxes))
        {
            throw new ArgumentException("The box does not belong to this tree's box tree.", nameof(textBox));
        }

        int i = Append(FragmentKind.Text, parent, textBox.Index, borderBox);
        _textStart[i] = textStart;
        _textLength[i] = textLength;
        _run[i] = run.Index;
        return new Fragment(this, i);
    }

    /// <summary>Stores a run of shaped glyphs and returns its id.</summary>
    /// <param name="face">The face the glyph ids belong to.</param>
    /// <param name="fontSize">The font size in CSS px.</param>
    /// <param name="textStart">Start of the source text, as a UTF-16 offset into the text box's text.</param>
    /// <param name="textLength">Length of the source text in UTF-16 units.</param>
    /// <param name="rightToLeft">True for an odd bidi level.</param>
    /// <param name="glyphIds">Glyph ids in visual order.</param>
    /// <param name="x">Each glyph's origin x relative to the start of the text fragment, same length as <paramref name="glyphIds"/>.</param>
    /// <param name="y">Each glyph's origin y relative to the baseline, same length.</param>
    /// <param name="clusters">For each glyph the UTF-16 offset of its cluster in the source text, same length.</param>
    /// <exception cref="ArgumentException">The spans differ in length.</exception>
    public GlyphRunId AddGlyphRun(FontFace face, double fontSize, int textStart, int textLength, bool rightToLeft, ReadOnlySpan<int> glyphIds, ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<int> clusters)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (x.Length != glyphIds.Length || y.Length != glyphIds.Length || clusters.Length != glyphIds.Length)
        {
            throw new ArgumentException("The glyph spans must have the same length.");
        }

        if (_runCount == _runs.Length)
        {
            Array.Resize(ref _runs, _runs.Length * 2);
        }

        if (_glyphCount + glyphIds.Length > _glyphId.Length)
        {
            int size = (int)Math.Min(Array.MaxLength, Math.Max((long)_glyphId.Length * 2, (long)_glyphCount + glyphIds.Length));
            Array.Resize(ref _glyphId, size);
            Array.Resize(ref _glyphX, size);
            Array.Resize(ref _glyphY, size);
            Array.Resize(ref _glyphCluster, size);
        }

        glyphIds.CopyTo(_glyphId.AsSpan(_glyphCount));
        x.CopyTo(_glyphX.AsSpan(_glyphCount));
        y.CopyTo(_glyphY.AsSpan(_glyphCount));
        clusters.CopyTo(_glyphCluster.AsSpan(_glyphCount));
        _runs[_runCount] = new GlyphRun(face, fontSize, _glyphCount, glyphIds.Length, textStart, textLength, rightToLeft);
        _glyphCount += glyphIds.Length;
        return new GlyphRunId(_runCount++);
    }

    /// <summary>Sets the resolved border widths and padding of a fragment.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    /// <param name="border">Border widths in px.</param>
    /// <param name="padding">Padding in px.</param>
    public void SetEdges(Fragment fragment, Insets border, Insets padding)
    {
        Check(fragment);
        _border[fragment.Index] = border;
        _padding[fragment.Index] = padding;
    }

    /// <summary>Replaces the border box of a fragment, for layouts that learn a box's size after placing its children.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    /// <param name="borderBox">The new border box, relative to the parent's border-box origin.</param>
    public void SetBorderBox(Fragment fragment, Rect borderBox)
    {
        Check(fragment);
        int i = fragment.Index;
        _x[i] = borderBox.X;
        _y[i] = borderBox.Y;
        _w[i] = borderBox.Width;
        _h[i] = borderBox.Height;
    }

    // For layouts in this assembly that learn a line's baseline after placing its content.
    internal void SetBaseline(Fragment fragment, double baseline)
    {
        Check(fragment);
        _baseline[fragment.Index] = baseline;
    }

    /// <summary>Sets the scrollable overflow area of a fragment, in the fragment's own coordinates.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    /// <param name="overflow">The overflow rectangle.</param>
    public void SetScrollableOverflow(Fragment fragment, Rect overflow)
    {
        Check(fragment);
        _overflow[fragment.Index] = overflow;
    }

    /// <summary>Marks how a fragment continues into or from its box's other fragments.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    /// <param name="traits">The flags.</param>
    public void SetTraits(Fragment fragment, FragmentTraits traits)
    {
        Check(fragment);
        _traits[fragment.Index] = (byte)traits;
    }

    // ---- reading ----

    /// <summary>The first fragment of a box, or the null handle if layout produced none (for example for <c>display: none</c> boxes).</summary>
    /// <param name="box">A box of this tree's box tree.</param>
    public Fragment FragmentOf(Box box) =>
        box.IsNull || !ReferenceEquals(box.Tree, Boxes) || (uint)box.Index >= (uint)_boxFirst.Length ? default : Make(_boxFirst[box.Index]);

    /// <summary>Reads a glyph run.</summary>
    /// <param name="id">A valid id from this tree.</param>
    public GlyphRun GetGlyphRun(GlyphRunId id) => _runs[id.Index];

    /// <summary>The glyph ids of a run.</summary>
    /// <param name="run">A run from this tree.</param>
    public ReadOnlySpan<int> GlyphIds(in GlyphRun run) => new(_glyphId, run.GlyphStart, run.GlyphCount);

    /// <summary>The glyph origins' x offsets of a run, relative to the start of its text fragment.</summary>
    /// <param name="run">A run from this tree.</param>
    public ReadOnlySpan<double> GlyphX(in GlyphRun run) => new(_glyphX, run.GlyphStart, run.GlyphCount);

    /// <summary>The glyph origins' y offsets of a run, relative to the baseline.</summary>
    /// <param name="run">A run from this tree.</param>
    public ReadOnlySpan<double> GlyphY(in GlyphRun run) => new(_glyphY, run.GlyphStart, run.GlyphCount);

    /// <summary>For each glyph of a run, the UTF-16 offset of its cluster in the source text.</summary>
    /// <param name="run">A run from this tree.</param>
    public ReadOnlySpan<int> GlyphClusters(in GlyphRun run) => new(_glyphCluster, run.GlyphStart, run.GlyphCount);

    /// <summary>The offset of a fragment's border-box origin from the document origin: the sum of the offsets of the fragment and all its ancestors.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    public (double X, double Y) AbsoluteOrigin(Fragment fragment)
    {
        Check(fragment);
        double x = 0, y = 0;
        for (int i = fragment.Index; i >= 0; i = _parent[i])
        {
            x += _x[i];
            y += _y[i];
        }

        return (x, y);
    }

    /// <summary>A fragment's border box in document coordinates.</summary>
    /// <param name="fragment">A fragment of this tree.</param>
    public Rect AbsoluteBorderBox(Fragment fragment)
    {
        (double x, double y) = AbsoluteOrigin(fragment);
        return new Rect(x, y, _w[fragment.Index], _h[fragment.Index]);
    }

    /// <summary>
    /// The border box of a styled node in document coordinates: the union of the border boxes of all fragments of its box and,
    /// for a split inline box, of every piece. This is what <c>getBoundingClientRect</c> reports at scroll position zero.
    /// </summary>
    /// <param name="node">A node of the box tree's styled tree.</param>
    /// <param name="borderBox">The border box.</param>
    /// <returns>False when the node generated no box or layout produced no fragment for it.</returns>
    public bool TryGetBorderBox(StyledNode node, out Rect borderBox)
    {
        borderBox = default;
        Box box = Boxes.PrincipalBox(node);
        bool found = false;
        while (!box.IsNull)
        {
            for (Fragment f = FragmentOf(box); !f.IsNull; f = f.NextOfBox)
            {
                Rect r = AbsoluteBorderBox(f);
                borderBox = found ? borderBox.Union(r) : r;
                found = true;
            }

            box = box.NextPiece;
        }

        return found;
    }

    /// <summary>The border box of a OneHtml node in document coordinates; see <see cref="TryGetBorderBox(StyledNode, out Rect)"/>.</summary>
    /// <param name="node">An element of the document the styled tree was built from.</param>
    /// <param name="borderBox">The border box.</param>
    /// <returns>False when the node is not from that document, generated no box, or has no fragment.</returns>
    public bool TryGetBorderBox(Node node, out Rect borderBox)
    {
        borderBox = default;
        StyledTree styled = Boxes.Styled;
        if (node.IsNull || !ReferenceEquals(node.Document, styled.SourceDocument))
        {
            return false;
        }

        StyledNode s = styled.FindBySource(node.Index);
        return !s.IsNull && TryGetBorderBox(s, out borderBox);
    }

    /// <summary>A text outline of the tree for tests: one fragment per line, two spaces of indent per level.</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        if (_count == 0)
        {
            return string.Empty;
        }

        int f = 0;
        int depth = 0;
        while (f >= 0)
        {
            sb.Append(' ', depth * 2);
            DumpFragment(sb, f);
            sb.Append('\n');
            if (_first[f] >= 0)
            {
                f = _first[f];
                depth++;
                continue;
            }

            while (f >= 0 && _next[f] < 0)
            {
                f = _parent[f];
                depth--;
            }

            if (f >= 0)
            {
                f = _next[f];
            }
        }

        return sb.ToString();
    }

    private void DumpFragment(StringBuilder sb, int i)
    {
        var rect = new Rect(_x[i], _y[i], _w[i], _h[i]);
        switch ((FragmentKind)_kind[i])
        {
            case FragmentKind.Line:
                sb.Append("line ").Append(rect.ToString()).Append(" baseline=").Append(_baseline[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case FragmentKind.Text:
                sb.Append("text [").Append(_textStart[i]).Append("..").Append(_textStart[i] + _textLength[i]).Append("] ").Append(rect.ToString());
                if (_run[i] >= 0)
                {
                    sb.Append(" glyphs=").Append(_runs[_run[i]].GlyphCount);
                }

                break;
            default:
            {
                Box box = Boxes.Make(_box[i]);
                StyledNode node = box.Node;
                sb.Append("box ").Append(node.IsNull ? "anon" : node.Kind == StyledNodeKind.Element ? node.TagName : node.Kind.ToString().ToLowerInvariant()).Append(' ').Append(rect.ToString());
                break;
            }
        }
    }

    // ---- column access for the handles ----

    internal Fragment Make(int index) => index < 0 ? default : new Fragment(this, index);

    internal byte KindOf(int i) => _kind[i];

    internal byte TraitsOf(int i) => _traits[i];

    internal int BoxOf(int i) => _box[i];

    internal int ParentOf(int i) => _parent[i];

    internal int FirstChildOf(int i) => _first[i];

    internal int LastChildOf(int i) => _last[i];

    internal int NextSiblingOf(int i) => _next[i];

    internal int NextOfBoxOf(int i) => _nextOfBox[i];

    internal Rect BorderBoxOf(int i) => new(_x[i], _y[i], _w[i], _h[i]);

    internal Insets BorderOf(int i) => _border[i];

    internal Insets PaddingOf(int i) => _padding[i];

    internal Rect OverflowOf(int i) => _overflow[i];

    internal double BaselineOf(int i) => _baseline[i];

    internal int TextStartOf(int i) => _textStart[i];

    internal int TextLengthOf(int i) => _textLength[i];

    internal GlyphRunId GlyphRunOf(int i) => new(_run[i]);

    private void Check(Fragment f)
    {
        if (f.IsNull || !ReferenceEquals(f.Tree, this))
        {
            throw new ArgumentException("The fragment does not belong to this tree.", nameof(f));
        }
    }

    private int Append(FragmentKind kind, Fragment parent, int box, Rect rect)
    {
        int p = -1;
        if (parent.IsNull)
        {
            if (_count != 0)
            {
                throw new ArgumentException("The tree already has a root fragment; give a parent.", nameof(parent));
            }
        }
        else
        {
            Check(parent);
            p = parent.Index;
        }

        if (_count == _kind.Length)
        {
            Grow();
        }

        int i = _count++;
        _kind[i] = (byte)kind;
        _traits[i] = 0;
        _box[i] = box;
        _parent[i] = p;
        _first[i] = -1;
        _last[i] = -1;
        _next[i] = -1;
        _nextOfBox[i] = -1;
        _textStart[i] = 0;
        _textLength[i] = 0;
        _run[i] = -1;
        _x[i] = rect.X;
        _y[i] = rect.Y;
        _w[i] = rect.Width;
        _h[i] = rect.Height;
        _baseline[i] = 0;
        _border[i] = default;
        _padding[i] = default;
        _overflow[i] = new Rect(0, 0, rect.Width, rect.Height);
        if (p >= 0)
        {
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

        return i;
    }

    private void Grow()
    {
        int n = _kind.Length * 2;
        Array.Resize(ref _kind, n);
        Array.Resize(ref _traits, n);
        Array.Resize(ref _box, n);
        Array.Resize(ref _parent, n);
        Array.Resize(ref _first, n);
        Array.Resize(ref _last, n);
        Array.Resize(ref _next, n);
        Array.Resize(ref _nextOfBox, n);
        Array.Resize(ref _textStart, n);
        Array.Resize(ref _textLength, n);
        Array.Resize(ref _run, n);
        Array.Resize(ref _x, n);
        Array.Resize(ref _y, n);
        Array.Resize(ref _w, n);
        Array.Resize(ref _h, n);
        Array.Resize(ref _baseline, n);
        Array.Resize(ref _border, n);
        Array.Resize(ref _padding, n);
        Array.Resize(ref _overflow, n);
    }
}
