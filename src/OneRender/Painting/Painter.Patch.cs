using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

// Rewriting part of the retained list in place.
//
// A full paint records, for every fragment, which items it wrote (a "span"). When a later layout changed few fragments and left every
// fragment where it was (same count, same kind, box and parent at every index), only those fragments and the boxes whose style or
// text was patched can paint differently. TryPatch paints just those again over their old slots. Anything that would not fit the old
// slots exactly (a different number of items, a new kind of scope) or that this path does not model returns false, and the caller
// paints the whole list.
public sealed partial class Painter
{
    // Per-fragment record of the last full paint.
    private int[] _spanStart = new int[64];
    private int[] _spanCount = new int[64];
    private byte[] _jflags = new byte[64];

    // Where the fragment sits in its paint owner's order: the phase list it is linked into and, for z-ordered entries, its z-index.
    private long[] _phaseOf = new long[64];

    // Set at the full paint: the fragment's scopes or neighbours make a rewrite unsafe.
    private const byte JUnpatchable = 1;

    // The box carries something whose paint depends on other boxes: text decoration on descendants' text, outlines painted by an owner.
    private const byte JDecoration = 2;
    private const byte JOutline = 4;

    // The box paints from data this path does not follow: columns, tables, sticky offsets, effects.
    private const byte JSpecial = 8;

    // Some fragment below this one was painted inside clips of containing blocks that lie above it in the tree (a stacking-context child
    // reached through this one). Those clips come from this fragment's geometry, so a change here may change what it painted.
    private const byte JBelow = 16;

    private int[] _rMark = new int[64];
    private int[] _rDone = new int[64];
    private int[] _rList = new int[64];
    private int[] _chain = new int[64];
    private int[] _tbMark = new int[64];
    private int _rStamp;
    private int _rCount;

    private RectF _canvasRect;
    private int _canvasStart;
    private int _canvasCount;
    private bool _noPatch;
    private bool _journalPending;
    private bool _journalValid;
    private FragmentTree? _journalTree;
    private RetainedDisplayList? _journalRetained;
    private int _journalVersion;
    private int _journalCount;
    private int _journalItems;
    private long _journalPayload;
    private PaintOptions _journalOptions;

    /// <summary>How many flushes rewrote the list in place (a diagnostic for tests and benchmarks).</summary>
    internal int PatchedPaints { get; private set; }

    /// <summary>The layout version the retained list was last painted or patched for.</summary>
    internal int JournalVersion => _journalVersion;

    private byte JournalFlagsOf(Box b, in ComputedValues v)
    {
        byte f = 0;
        if (v.TextDecorationLine != TextDecorationLines.None)
        {
            f |= JDecoration;
        }

        if (v.OutlineWidth > 0 && (v.OutlineStyle != BorderStyleKind.None || v.OutlineAuto))
        {
            f |= JOutline;
        }

        if (v.Control != Forms.ControlKind.None)
        {
            // Controls paint from their state and from other boxes (a fieldset's legend, an open list in the top layer).
            _noPatch = true;
        }

        if (v.IsMulticol || b.Context is FormattingContext.Table or FormattingContext.TableCell || v.Position == PositionKind.Sticky || v.CreatesEffectContext)
        {
            f |= JSpecial;
            if (v.IsMulticol || b.Context is FormattingContext.Table or FormattingContext.TableCell || v.Position == PositionKind.Sticky)
            {
                // Their paint (column rules, collapsed borders, sticky offsets) reads other fragments or is cached outside the list.
                _noPatch = true;
            }
        }

        return f;
    }

    private void RecordEnter(int top, int start, int entriesBefore)
    {
        ref Frame fr = ref _stack[top];
        int f = fr.Fragment;
        _spanStart[f] = start;
        _spanCount[f] = _list.Count - start;
        if (fr.PreClips > 0 || _patchCount != entriesBefore || fr.Group || fr.Host || fr.Outer || fr.Scroll || fr.FxOuter != 0 || fr.FxInner != 0)
        {
            _jflags[f] |= JUnpatchable;
        }

        if (fr.PreClips > 0)
        {
            for (int a = _tree.ParentOf(f); a >= 0 && (_jflags[a] & JBelow) == 0; a = _tree.ParentOf(a))
            {
                _jflags[a] |= JBelow;
            }
        }
    }

    private void RecordJournal(FragmentTree fragments, RetainedDisplayList retained, in PaintOptions options)
    {
        _journalValid = true;
        _journalTree = fragments;
        _journalRetained = retained;
        _journalVersion = fragments.Version;
        _journalCount = fragments.Count;
        _journalItems = retained.Current.Count;
        _journalPayload = retained.Current.PayloadSize;
        _journalOptions = options;
        retained.Current.ReserveHeadroom();
    }

    private bool SameOptions(in PaintOptions o) =>
        o.ViewportWidth == _journalOptions.ViewportWidth && o.ViewportHeight == _journalOptions.ViewportHeight
        && ReferenceEquals(o.Scroll, _journalOptions.Scroll) && ReferenceEquals(o.Images, _journalOptions.Images)
        && ReferenceEquals(o.Fonts, _journalOptions.Fonts) && o.DevicePixelRatio == _journalOptions.DevicePixelRatio
        && !o.Tags && o.Editing is null && _journalOptions.Editing is null && string.Equals(o.BaseUrl, _journalOptions.BaseUrl, StringComparison.Ordinal);

    /// <summary>
    /// Brings the retained list up to date by painting again only the fragments that can look different, over the items they wrote
    /// last time. The damage of every changed item is added to <see cref="RetainedDisplayList.Damage"/>.
    /// </summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="retained">The list the last paint through this painter produced.</param>
    /// <param name="options">The options of the last paint.</param>
    /// <param name="delta">What layout changed; ignored unless <paramref name="layoutRan"/>.</param>
    /// <param name="layoutRan">True when a layout ran since the last paint.</param>
    /// <returns>False when the list was left as it was or partly rewritten and the caller must paint it in full (the damage so far stays valid).</returns>
    internal bool TryPatch(FragmentTree fragments, RetainedDisplayList retained, in PaintOptions options, LayoutDelta? delta, bool layoutRan)
    {
        if (!_journalValid || !ReferenceEquals(_journalTree, fragments) || !ReferenceEquals(_journalRetained, retained) || Observer is not null
            || !SameOptions(in options) || fragments.Count != _journalCount || fragments.Version != _journalVersion + (layoutRan ? 1 : 0)
            || (layoutRan && (delta is null || !delta.Aligned)))
        {
            return false;
        }

        BoxTree boxes = fragments.Boxes;
        DisplayList list = retained.Current;
        if (boxes.TouchedOverflow || list.Count != _journalItems || list.PayloadSize > (2 * _journalPayload) + 4096 || list.Rewriting)
        {
            return false;
        }

        _tree = fragments;
        _boxes = boxes;
        _list = list;
        try
        {
            bool ok = Patch(retained, delta, layoutRan);
            if (!ok)
            {
                _journalValid = false;
                return false;
            }

            _journalVersion = fragments.Version;
            PatchedPaints++;
            if (Diagnostics.Items.Count > 0 || Diagnostics.Dropped > 0)
            {
                _journalValid = false;
            }

            return true;
        }
        finally
        {
            _tree = null!;
            _boxes = null!;
            _list = null!;
        }
    }

    private bool Patch(RetainedDisplayList retained, LayoutDelta? delta, bool layoutRan)
    {
        _images = _journalOptions.Images;
        _fonts = _journalOptions.Fonts;
        _dpr = _journalOptions.DevicePixelRatio > 0 && double.IsFinite(_journalOptions.DevicePixelRatio) ? _journalOptions.DevicePixelRatio : 1;
        _scroll = _journalOptions.Scroll;
        _vw = _journalOptions.ViewportWidth;
        _vh = _journalOptions.ViewportHeight;
        _scopes = 0;
        if (layoutRan)
        {
            ViewportOverflow.Resolve(_boxes, out _, out _, out int source);
            if (source != _vpSource)
            {
                return false;
            }

            if (_scroll is not null)
            {
                // The viewport scroll range follows the document's extent. The entries that clamp against it take the new range;
                // whether the document scrolls at all decides which items exist, so that cannot change.
                ScrollRange range = ScrollState.GetViewportRange(_tree, _vw, _vh);
                if (!range.Equals(_vpRange))
                {
                    if (range.CanScroll != _vpRange.CanScroll)
                    {
                        return false;
                    }

                    _vpRange = range;
                    for (int k = 0; k < _patchCount; k++)
                    {
                        if (_patch[k].Kind is PatchViewportKind or PatchFixedKind)
                        {
                            _patch[k].Range = range;
                        }
                    }
                }
            }
        }

        // The fragments that can paint differently.
        if (++_rStamp == int.MaxValue)
        {
            Array.Clear(_rMark);
            Array.Clear(_rDone);
            Array.Clear(_tbMark);
            _rStamp = 1;
        }

        _rCount = 0;
        int limit = _n / 2;
        bool canvasMoved = false;
        if (layoutRan)
        {
            for (int k = 0; k < delta!.Count; k++)
            {
                int i = delta.IndexAt(k);
                byte dflags = delta.FlagsAt(k);
                if (i == 0 && !_canvasRect.Equals(CanvasRect(_vw, _vh)))
                {
                    // The canvas background spans the root's border box and overflow and is painted outside any fragment's span.
                    canvasMoved = true;
                }

                if (dflags == LayoutDelta.OverflowOnly)
                {
                    // Only boxes that clip or scroll read their overflow (whether they can scroll decides their scope).
                    if ((_flags[i] & FlagScroller) != 0)
                    {
                        Mark(i);
                    }

                    continue;
                }

                Mark(i);
                if ((dflags & LayoutDelta.Moved) != 0)
                {
                    MarkSubtree(i);
                }

                if (_rCount > limit)
                {
                    return false;
                }
            }
        }

        if (canvasMoved)
        {
            if (_canvasCount == 0)
            {
                // No canvas item exists; only the remembered extent moves.
                _canvasRect = CanvasRect(_vw, _vh);
            }
            else
            {
                // A plain colored canvas is one item: it is painted again over its slot with the new extent.
                if (_canvasCount != 1)
                {
                    return false;
                }

                _list.BeginRewrite(_canvasStart, 1, retained.Damage);
                PaintCanvas(_journalOptions);
                if (!_list.EndRewrite())
                {
                    return false;
                }
            }
        }

        BoxTree boxes = _boxes;
        bool textTouched = false;
        for (int t = 0; t < boxes.TouchedCount; t++)
        {
            int tb = boxes.TouchedAt(t);
            if (boxes.KindOf(tb) == (byte)BoxKind.Text)
            {
                // Text fragments are not chained by box; they are found by one scan below.
                if (_tbMark.Length < boxes.Count)
                {
                    _tbMark = new int[Math.Max(boxes.Count, _tbMark.Length * 2)];
                }

                _tbMark[tb] = _rStamp;
                textTouched = true;
                continue;
            }

            for (int f = _tree.FirstFragmentOfBox(tb); f >= 0; f = _tree.NextOfBoxOf(f))
            {
                Mark(f);
            }

            if (_rCount > limit)
            {
                return false;
            }
        }

        if (textTouched)
        {
            for (int f = 0; f < _n; f++)
            {
                if ((FragmentKind)_tree.KindOf(f) == FragmentKind.Text && _tbMark[_tree.BoxOf(f)] == _rStamp)
                {
                    Mark(f);
                }
            }

            if (_rCount > limit)
            {
                return false;
            }
        }

        // Geometry and classification again, parents before children.
        for (int k = 0; k < _rCount; k++)
        {
            int c = _rList[k];
            int sp = 0;
            while (c >= 0 && _rMark[c] == _rStamp && _rDone[c] != _rStamp)
            {
                _chain[sp++] = c;
                c = _tree.ParentOf(c);
            }

            while (sp > 0)
            {
                int x = _chain[--sp];
                _rDone[x] = _rStamp;
                if (!Reclassify(x))
                {
                    return false;
                }
            }
        }

        // Paint them again over their old slots.
        DamageRegion damage = retained.Damage;
        for (int k = 0; k < _rCount; k++)
        {
            if (!Rewrite(_rList[k], damage))
            {
                return false;
            }
        }

        return true;
    }

    private void Mark(int i)
    {
        if (_rMark[i] == _rStamp)
        {
            return;
        }

        _rMark[i] = _rStamp;
        _rList[_rCount++] = i;
    }

    private void MarkSubtree(int root)
    {
        int c = _tree.FirstChildOf(root);
        while (c >= 0)
        {
            Mark(c);
            int f = _tree.FirstChildOf(c);
            if (f >= 0)
            {
                c = f;
                continue;
            }

            while (true)
            {
                int next = _tree.NextSiblingOf(c);
                if (next >= 0)
                {
                    c = next;
                    break;
                }

                c = _tree.ParentOf(c);
                if (c == root || c < 0)
                {
                    c = -1;
                    break;
                }
            }
        }
    }

    private bool Reclassify(int x)
    {
        if (x == 0 || x == _skipBackground)
        {
            // The canvas background belongs to the root and body boxes and is painted outside any fragment's span.
            return false;
        }

        byte flags = _flags[x];
        int owner = _ownerFor[x], context = _contextFor[x];
        long phase = _phaseOf[x];
        VisitCore(x, false);
        if (_flags[x] != flags || _ownerFor[x] != owner || _contextFor[x] != context || _phaseOf[x] != phase)
        {
            return false;
        }

        if ((FragmentKind)_tree.KindOf(x) == FragmentKind.Box)
        {
            Box b = _boxes.Make(_tree.BoxOf(x));
            byte now = JournalFlagsOf(b, in b.Values);
            if (((_jflags[x] | now) & (JUnpatchable | JDecoration | JOutline | JSpecial | JBelow)) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool Rewrite(int x, DamageRegion damage)
    {
        FragmentKind kind = (FragmentKind)_tree.KindOf(x);
        int start = _spanStart[x];
        if (start < 0)
        {
            // Lines hold no items; a box or text that painted nothing and had no entry cannot start now.
            return kind == FragmentKind.Line || (kind == FragmentKind.Text && (_flags[x] & FlagIsText) == 0);
        }

        _list.BeginRewrite(start, _spanCount[x], damage);
        int entries = _patchCount;
        bool ok;
        if ((_flags[x] & FlagAtomic) != 0)
        {
            _stack[0] = new Frame { Fragment = x, Phase = -1, Cursor = -1 };
            Enter(0);
            ref Frame fr = ref _stack[0];
            ok = !(fr.Group || fr.Outer || fr.Scroll || fr.FxOuter != 0 || fr.FxInner != 0) && _patchCount == entries;
        }
        else
        {
            if ((_flags[x] & FlagIsText) != 0)
            {
                PaintText(x);
            }
            else
            {
                PaintBoxDecoration(x);
            }

            ok = true;
        }

        return _list.EndRewrite() && ok;
    }
}
