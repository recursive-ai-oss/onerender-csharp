using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    // ---------------------------------------------------------------------------------------------------------------------
    // Reuse of an earlier layout (the engine side of incremental layout).
    //
    // A block formatting context root whose subtree did not change, laid out with the same inputs as last time, produces the same
    // fragments again: its children depend on nothing but the root's own frame (widths, heights, flags) and on the box subtree.
    // When the root is opened, the engine compares its frame with what it recorded when it last laid the box out. On a match it
    // copies the earlier children fragments, restores the few frame values the children would have left behind, and goes on to
    // close the box as usual. Subtrees that reach outside themselves (floats, out-of-flow positioning, columns, tables) are never
    // reused; see BoxTree hazards.
    //
    // One record per box describes the box's fragments in the published tree. A layout that lays the box out again replaces the
    // record; a layout that copies it (or an ancestor) leaves it, because the copy is the same. Records written during a layout
    // describe the tree being built, so that layout does not read them (Gen), and records of boxes that changed are dropped first.
    // ---------------------------------------------------------------------------------------------------------------------

    private readonly record struct MemoKey(
        double W,
        double ContentLeft,
        double ContentTop,
        double BottomEdge,
        double ContentWidth,
        double SpecH,
        double MinH,
        double MaxH,
        double RatioH,
        ushort Flags,
        int GridDepth,
        int TableDepth,
        int Depth);

    private struct MemoEntry
    {
        public bool Valid;
        public int Gen;
        public int Frag;
        public MemoKey Key;
        public double Cursor;
        public double SpecH;
        public double MinH;
        public double MaxH;
        public double RatioH;
        public double FirstBaseline;
        public double OvL;
        public double OvT;
        public double OvR;
        public double OvB;
        public double Pos;
        public double Neg;
        public ushort FlagsAdded;
    }

    private const FrameFlags MemoFlagMask = ~(FrameFlags.TopPending | FrameFlags.Cleared | FrameFlags.ClearsFloats | FrameFlags.HasContent | FrameFlags.AnyChild);

    private MemoEntry[] _m = [];
    private int[] _log = new int[64];
    private int _logCount;
    private int _gen;
    private int _base;
    private bool _reuse;
    // Multi-column containers currently open. Their columns are cut after the whole layout, so fragments inside one are rewritten
    // by the cut and can neither be copied from an earlier layout nor recorded for one.
    private int _mcOpen;
    private bool _recording;
    private bool _memoValid;
    private BoxTree? _memoBoxes;
    private FragmentTree? _memoOut;
    private IFontProvider? _memoFonts;
    private int _memoFontVersion;
    private int _memoEpoch;
    private FragmentTree? _scratch;
    private FragmentTree? _prevTree;
    private int _hits;
    private int _stores;

    /// <summary>How many subtrees the last layout took from the layout before it (a diagnostic for tests and tooling).</summary>
    public int ReusedSubtrees => _hits;

    private void BeginReuse(BoxTree boxes, in LayoutInput input, FragmentTree output)
    {
        bool same = _memoValid && ReferenceEquals(_memoBoxes, boxes) && _memoEpoch == boxes.Epoch && ReferenceEquals(_memoFonts, input.Fonts)
            && ReferenceEquals(_memoOut, output) && _memoFontVersion == input.Fonts.Catalog.Version && _m.Length >= boxes.Count;
        _memoValid = false;
        if (_scratch is null || !ReferenceEquals(_scratch.Boxes, boxes))
        {
            _scratch = new FragmentTree(boxes);
            same = false;
        }

        if (_m.Length < boxes.Count)
        {
            _m = new MemoEntry[boxes.Count + (boxes.Count / 2) + 1];
            same = false;
        }

        if (++_gen == int.MaxValue)
        {
            Array.Clear(_m);
            _gen = 1;
            same = false;
        }

        _logCount = 0;
        if (same)
        {
            // What the boxes that changed laid out to is no longer described by their records.
            for (int i = 0; i < boxes.DirtyCount; i++)
            {
                _m[boxes.DirtyAt(i)].Valid = false;
            }
        }
        else
        {
            // Nothing from before is usable.
            _base = _gen;
        }

        _reuse = same;
        _recording = true;
        _mcOpen = 0;
        _prevTree = same ? output : null;
        _hits = 0;
        _stores = 0;
        _out = _scratch;
    }

    /// <summary>How the last reusing layout differs from the one before it; paint uses it to rewrite only what changed.</summary>
    internal LayoutDelta Delta { get; } = new();

    private void EndReuse(BoxTree boxes, in LayoutInput input, FragmentTree output)
    {
        if (_prevTree is not null)
        {
            _scratch!.DiffAgainst(output, Delta);
        }
        else
        {
            Delta.Reset(false);
        }

        output.SwapContents(_scratch!);
        _memoBoxes = boxes;
        _memoOut = output;
        _memoFonts = input.Fonts;
        _memoFontVersion = input.Fonts.Catalog.Version;
        _memoEpoch = boxes.Epoch;
        _memoValid = true;
        _reuse = false;
        _recording = false;
        _prevTree = null;
        _out = output;
    }

    // The intrinsic sizes of a box depend on its subtree only, so those of clean boxes stay valid between layouts.
    private void PrepareIntrinsicCacheForReuse(int boxCount)
    {
        if (!_reuse || _minContent.Length < boxCount)
        {
            PrepareIntrinsicCache(boxCount);
            return;
        }

        for (int i = 0; i < _boxes.DirtyCount; i++)
        {
            int b = _boxes.DirtyAt(i);
            _minContent[b] = double.NaN;
            _maxContent[b] = double.NaN;
        }

        _isp = 0;
    }

    private bool MemoEligible(Box c, in Frame nf)
    {
        return _recording
            && _mcOpen == 0
            && c.Kind == BoxKind.Container
            && (c.Context is FormattingContext.Block or FormattingContext.FlowRoot or FormattingContext.Flex || (c.Context == FormattingContext.Grid && !IsSubgridBox(c)))
            && (nf.Flags & (FrameFlags.Multicol | FrameFlags.Flex | FrameFlags.Float)) == 0
            && _boxes.HazardOf(c.Index) == 0
            && _gridIntrinsicMode == 0
            && _sp < 50_000;
    }

    // A subgrid takes its tracks from the grid around it, which the frame key does not describe.
    private static bool IsSubgridBox(Box c) => c.Values.GridTemplateColumns.IsSubgrid || c.Values.GridTemplateRows.IsSubgrid;

    private MemoKey KeyOf(in Frame f) => new(
        f.W,
        f.ContentLeft,
        f.ContentTop,
        f.BottomEdge,
        f.ContentWidth,
        f.SpecH,
        f.MinH,
        f.MaxH,
        f.RatioH,
        (ushort)(f.Flags & MemoFlagMask),
        _gridDepth,
        _tableDepth,
        _depth);

    /// <summary>
    /// Called when a block formatting context root has just been opened. Returns true when its children were taken from the earlier
    /// layout (so the caller skips laying them out); in any case the frame is marked so that its result is recorded when it closes.
    /// </summary>
    private bool MemoOpen(int ni, Box c)
    {
        ref Frame f = ref _frames[ni];
        f.MemoRec = false;
        if (!_recording)
        {
            return false;
        }

        if (!MemoEligible(c, in f))
        {
            // Laid out in a way the record does not describe: it must not be used for a later copy.
            _m[c.Index].Valid = false;
            return false;
        }

        MemoKey key = KeyOf(in f);
        f.MemoKey = key;
        // A trial layout is undone, so there is nothing to record.
        f.MemoRec = _trialNesting == 0;
        if (_prevTree is null || _boxes.IsDirty(c.Index))
        {
            return false;
        }

        ref MemoEntry e = ref _m[c.Index];
        if (!e.Valid || e.Gen < _base || e.Gen == _gen || !e.Key.Equals(key))
        {
            return false;
        }

        FragmentTree prev = _prevTree;
        int pf = prev.FirstFragmentOfBox(c.Index);
        if (pf < 0 || prev.BoxColumnOf(pf) != c.Index || prev.NextOfBoxOf(pf) >= 0)
        {
            return false;
        }

        _out.CopyDescendants(prev, pf, f.Frag);
        f.Cursor = e.Cursor;
        // A flex container turns its height into a definite one while it closes.
        f.SpecH = e.SpecH;
        f.MinH = e.MinH;
        f.MaxH = e.MaxH;
        f.RatioH = e.RatioH;
        f.FirstBaseline = e.FirstBaseline;
        f.OvL = e.OvL;
        f.OvT = e.OvT;
        f.OvR = e.OvR;
        f.OvB = e.OvB;
        f.Flags |= (FrameFlags)e.FlagsAdded;
        f.NextChild = -1;
        _pos = e.Pos;
        _neg = e.Neg;
        _hits++;
        // The record still describes the copy.
        f.MemoRec = false;
        return true;
    }

    private void MemoPut(int box, in MemoEntry entry)
    {
        _m[box] = entry;
        _m[box].Valid = true;
        _m[box].Gen = _gen;
        if (_logCount == _log.Length)
        {
            Array.Resize(ref _log, _log.Length * 2);
        }

        _log[_logCount++] = box;
    }

    // Called as a frame starts to close: its children are final, so what they left in the frame is what a reuse must restore.
    private void MemoClose(in Frame t)
    {
        var e = new MemoEntry
        {
            Frag = t.Frag,
            Key = t.MemoKey,
            Cursor = t.Cursor,
            SpecH = t.SpecH,
            MinH = t.MinH,
            MaxH = t.MaxH,
            RatioH = t.RatioH,
            FirstBaseline = t.FirstBaseline,
            OvL = t.OvL,
            OvT = t.OvT,
            OvR = t.OvR,
            OvB = t.OvB,
            Pos = _pos,
            Neg = _neg,
            FlagsAdded = (ushort)(t.Flags & (FrameFlags.HasContent | FrameFlags.AnyChild)),
        };
        MemoPut(t.Box, in e);
        _stores++;
    }

    // A trial layout was undone: records written since point at fragments that no longer exist.
    private void MemoTruncate(int fragmentCount)
    {
        if (!_recording)
        {
            return;
        }

        while (_logCount > 0)
        {
            int box = _log[_logCount - 1];
            if (_m[box].Frag < fragmentCount)
            {
                break;
            }

            _m[box].Valid = false;
            _logCount--;
        }
    }
}
