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

    // Set at the full paint: the fragment's scopes or neighbours make a rewrite unsafe.
    private const byte JUnpatchable = 1;

    // The box carries something whose paint depends on other boxes: text decoration on descendants' text, outlines painted by an owner.
    private const byte JDecoration = 2;
    private const byte JOutline = 4;

    // The box paints from data this path does not follow: columns, tables, sticky offsets, effects.
    private const byte JSpecial = 8;

    private int[] _rMark = new int[64];
    private int[] _rDone = new int[64];
    private int[] _rList = new int[64];
    private int[] _chain = new int[64];
    private int _rStamp;
    private int _rCount;

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
        if (fr.PreClips > 0 || _patchCount != entriesBefore || fr.Group || fr.Outer || fr.Scroll || fr.FxOuter != 0 || fr.FxInner != 0)
        {
            _jflags[f] |= JUnpatchable;
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
        && !o.Tags && string.Equals(o.BaseUrl, _journalOptions.BaseUrl, StringComparison.Ordinal);

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
            _rStamp = 1;
        }

        _rCount = 0;
        int limit = _n / 2;
        if (layoutRan)
        {
            for (int k = 0; k < delta!.Count; k++)
            {
                int i = delta.IndexAt(k);
                byte dflags = delta.FlagsAt(k);
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

        BoxTree boxes = _boxes;
        for (int t = 0; t < boxes.TouchedCount; t++)
        {
            for (int f = _tree.FirstFragmentOfBox(boxes.TouchedAt(t)); f >= 0; f = _tree.NextOfBoxOf(f))
            {
                Mark(f);
                // Text paints from its own box's values, which follow an ancestor's inherited color, font and visibility.
                MarkTextBelow(f);
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

    private void MarkTextBelow(int root)
    {
        int c = _tree.FirstChildOf(root);
        while (c >= 0)
        {
            if ((FragmentKind)_tree.KindOf(c) == FragmentKind.Text)
            {
                Mark(c);
            }

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

            if (_rCount > _n / 2)
            {
                return;
            }
        }
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
        VisitCore(x, false);
        if (_flags[x] != flags || _ownerFor[x] != owner || _contextFor[x] != context)
        {
            return false;
        }

        if ((FragmentKind)_tree.KindOf(x) == FragmentKind.Box)
        {
            Box b = _boxes.Make(_tree.BoxOf(x));
            byte now = JournalFlagsOf(b, in b.Values);
            if (((_jflags[x] | now) & (JUnpatchable | JDecoration | JOutline | JSpecial)) != 0)
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
