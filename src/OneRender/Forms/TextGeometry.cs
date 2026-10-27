using OneRender.Layout;
using OneRender.Tree;

namespace OneRender.Forms;

/// <summary>
/// Answers "where is offset N on screen" and "which offset is at this point" for the text an editable control shows, from the fragments
/// layout produced for its text node. All coordinates are document space (the space of <see cref="Fragment.BorderBox"/> sums), before the
/// control's own scrolling. Offsets are into the text the control displays (the value with any preedit, password-masked).
/// </summary>
internal readonly struct TextGeometry
{
    private readonly FragmentTree _tree;
    private readonly StyledNode _text;
    private readonly StyledNode _control;
    private readonly double _fallbackHeight;
    private readonly bool _multiline;

    public TextGeometry(FragmentTree tree, StyledNode control, StyledNode text, double fallbackLineHeight, bool multiline)
    {
        _tree = tree;
        _control = control;
        _text = text;
        _fallbackHeight = fallbackLineHeight;
        _multiline = multiline;
    }

    public bool IsValid => _tree is not null && !_control.IsNull;

    /// <summary>The content box of the control in document space, or false when it has no fragment.</summary>
    public bool TryContentRect(out Rect content)
    {
        content = default;
        if (!IsValid || !_tree.TryGetBorderBox(_control, out Rect bb))
        {
            return false;
        }

        Fragment f = _tree.FragmentOf(_tree.Boxes.PrincipalBox(_control));
        if (f.IsNull)
        {
            return false;
        }

        Insets b = f.Border, p = f.Padding;
        content = new Rect(bb.X + b.Left + p.Left, bb.Y + b.Top + p.Top, Math.Max(0, bb.Width - b.Horizontal - p.Horizontal), Math.Max(0, bb.Height - b.Vertical - p.Vertical));
        return true;
    }

    // Text fragments are not linked to their box, so they are found under the control's own fragment, in tree order.
    private int FirstFragment()
    {
        if (!IsValid || _text.IsNull)
        {
            return -1;
        }

        Fragment root = _tree.FragmentOf(_tree.Boxes.PrincipalBox(_control));
        Box tb = _tree.Boxes.PrincipalBox(_text);
        if (root.IsNull || tb.IsNull)
        {
            return -1;
        }

        return Scan(root.Index, root.Index, tb.Index);
    }

    /// <summary>The next text fragment of the control's text after <paramref name="i"/>, or -1.</summary>
    public int NextFragment(int i)
    {
        Fragment root = _tree.FragmentOf(_tree.Boxes.PrincipalBox(_control));
        Box tb = _tree.Boxes.PrincipalBox(_text);
        return root.IsNull || tb.IsNull ? -1 : Scan(NextPre(i, root.Index), root.Index, tb.Index);
    }

    private int Scan(int from, int root, int textBox)
    {
        for (int i = from; i >= 0; i = NextPre(i, root))
        {
            if (_tree.BoxOf(i) == textBox && (FragmentKind)_tree.KindOf(i) == FragmentKind.Text)
            {
                return i;
            }
        }

        return -1;
    }

    private int NextPre(int i, int root)
    {
        int c = _tree.FirstChildOf(i);
        if (c >= 0)
        {
            return c;
        }

        while (i >= 0 && i != root)
        {
            int n = _tree.NextSiblingOf(i);
            if (n >= 0)
            {
                return n;
            }

            i = _tree.ParentOf(i);
        }

        return -1;
    }

    /// <summary>Where a caret at <paramref name="offset"/> sits: left edge x, top y and height, in document space.</summary>
    public bool TryCaret(int offset, out double x, out double y, out double height)
    {
        int f = FirstFragment();
        int last = -1;
        for (int i = f; i >= 0; i = NextFragment(i))
        {
            int start = _tree.TextStartOf(i), end = start + _tree.TextLengthOf(i);
            last = i;
            if (offset >= start && offset < end)
            {
                return CaretIn(i, offset, out x, out y, out height);
            }
        }

        if (last >= 0)
        {
            int start = _tree.TextStartOf(last), end = start + _tree.TextLengthOf(last);
            if (offset >= start && offset <= end)
            {
                bool ends = end > start && TextCharAt(last, end - 1) == '\n';
                if (!ends)
                {
                    return CaretIn(last, offset, out x, out y, out height);
                }

                // After a trailing line break the caret is at the start of a line that has no fragment: one line below the last.
                CaretIn(last, start, out _, out y, out height);
                x = TryContentRect(out Rect content) ? content.X : 0;
                y += LineAdvance(last);
                return true;
            }
        }

        return Fallback(out x, out y, out height);
    }

    private char TextCharAt(int fragment, int offset)
    {
        ReadOnlySpan<char> all = _text.Text;
        return (uint)offset < (uint)all.Length ? all[offset] : '\0';
    }

    private double LineAdvance(int textFragment)
    {
        int line = _tree.ParentOf(textFragment);
        if (line >= 0 && (FragmentKind)_tree.KindOf(line) == FragmentKind.Line)
        {
            return _tree.BorderBoxOf(line).Height;
        }

        return _tree.BorderBoxOf(textFragment).Height;
    }

    private bool Fallback(out double x, out double y, out double height)
    {
        x = y = height = 0;
        if (!TryContentRect(out Rect c))
        {
            return false;
        }

        height = _fallbackHeight;
        x = c.X;
        y = _multiline ? c.Y : c.Y + Math.Max(0, (c.Height - height) / 2);
        return true;
    }

    private bool CaretIn(int f, int offset, out double x, out double y, out double height)
    {
        Fragment frag = _tree.Make(f);
        Rect bb = _tree.AbsoluteBorderBox(frag);
        x = bb.X + LocalX(f, offset);
        y = bb.Y;
        height = bb.Height;
        return true;
    }

    /// <summary>The x of the boundary before <paramref name="offset"/>, from the left edge of fragment <paramref name="f"/>.</summary>
    public double LocalX(int f, int offset)
    {
        int start = _tree.TextStartOf(f), end = start + _tree.TextLengthOf(f);
        GlyphRunId id = _tree.GlyphRunOf(f);
        double width = _tree.BorderBoxOf(f).Width;
        if (!id.IsValid)
        {
            return 0;
        }

        GlyphRun run = _tree.GetGlyphRun(id);
        ReadOnlySpan<double> xs = _tree.GlyphX(in run);
        ReadOnlySpan<int> clusters = _tree.GlyphClusters(in run);
        if (xs.Length == 0)
        {
            return 0;
        }

        if (run.RightToLeft)
        {
            if (offset <= start)
            {
                return width;
            }

            if (offset >= end)
            {
                return 0;
            }

            for (int k = 0; k < xs.Length; k++)
            {
                if (clusters[k] == offset)
                {
                    return k + 1 < xs.Length ? xs[k + 1] : width;
                }
            }

            return width;
        }

        if (offset >= end)
        {
            return width;
        }

        int best = -1;
        for (int k = 0; k < xs.Length; k++)
        {
            int c = clusters[k];
            if (c <= offset && (best < 0 || c >= clusters[best]))
            {
                best = k;
            }
        }

        return best < 0 ? 0 : xs[best];
    }

    /// <summary>The text offset nearest to a point (document space).</summary>
    public int OffsetAt(double x, double y)
    {
        int f = FirstFragment();
        if (f < 0)
        {
            return 0;
        }

        int pick = f;
        double bestDist = double.MaxValue;
        for (int i = f; i >= 0; i = NextFragment(i))
        {
            Rect bb = _tree.AbsoluteBorderBox(_tree.Make(i));
            double dist = y < bb.Y ? bb.Y - y : y > bb.Bottom ? y - bb.Bottom : 0;
            if (dist < bestDist)
            {
                bestDist = dist;
                pick = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }

        return OffsetOnFragment(pick, x);
    }

    private int OffsetOnFragment(int f, double docX)
    {
        Rect bb = _tree.AbsoluteBorderBox(_tree.Make(f));
        int start = _tree.TextStartOf(f), end = start + _tree.TextLengthOf(f);
        int max = end > start && TextCharAt(f, end - 1) == '\n' ? end - 1 : end;
        GlyphRunId id = _tree.GlyphRunOf(f);
        if (!id.IsValid)
        {
            return start;
        }

        GlyphRun run = _tree.GetGlyphRun(id);
        ReadOnlySpan<double> xs = _tree.GlyphX(in run);
        ReadOnlySpan<int> clusters = _tree.GlyphClusters(in run);
        double lx = docX - bb.X;
        if (xs.Length == 0)
        {
            return start;
        }

        // Visit each cluster's boundaries in logical order and take the nearest to the point.
        int result = start;
        double bestDist = double.MaxValue;
        for (int k = 0; k < xs.Length; k++)
        {
            int c = Math.Clamp(clusters[k], start, max);
            double edge = run.RightToLeft ? (k + 1 < xs.Length ? xs[k + 1] : bb.Width) : xs[k];
            double d = Math.Abs(lx - edge);
            if (d < bestDist)
            {
                bestDist = d;
                result = c;
            }
        }

        double endEdge = run.RightToLeft ? 0 : bb.Width;
        if (Math.Abs(lx - endEdge) < bestDist)
        {
            result = max;
        }

        return Math.Clamp(result, start, max);
    }

    /// <summary>The offset where the visual line containing <paramref name="offset"/> starts.</summary>
    public int LineStart(int offset)
    {
        int f = FragmentAt(offset);
        return f < 0 ? 0 : _tree.TextStartOf(f);
    }

    /// <summary>The offset where the visual line containing <paramref name="offset"/> ends (before a hard line break).</summary>
    public int LineEnd(int offset)
    {
        int f = FragmentAt(offset);
        if (f < 0)
        {
            return 0;
        }

        int end = _tree.TextStartOf(f) + _tree.TextLengthOf(f);
        return end > _tree.TextStartOf(f) && TextCharAt(f, end - 1) is '\n' ? end - 1 : end;
    }

    private int FragmentAt(int offset)
    {
        int last = -1;
        for (int i = FirstFragment(); i >= 0; i = NextFragment(i))
        {
            int start = _tree.TextStartOf(i), end = start + _tree.TextLengthOf(i);
            last = i;
            if (offset >= start && offset < end)
            {
                return i;
            }
        }

        return last;
    }

    /// <summary>The offset one visual line up or down at the given x, or <paramref name="offset"/> when there is no such line.</summary>
    public int MoveVertical(int offset, int lines, double goalX)
    {
        if (!TryCaret(offset, out _, out double y, out double h))
        {
            return offset;
        }

        double probe = lines > 0 ? y + h + (h / 2) : y - (h / 2);
        int target = OffsetAt(goalX, probe);
        if (lines > 0 && target <= offset)
        {
            // Past the last line the caret goes to the end; before the first, to the start.
            return LastLineContains(offset) ? offset : target;
        }

        if (lines < 0 && target >= offset)
        {
            return offset;
        }

        return target;
    }

    private bool LastLineContains(int offset)
    {
        int f = FragmentAt(offset);
        return f >= 0 && NextFragment(f) < 0;
    }
}
