using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    // ---------------------------------------------------------------------------------------------------------------------
    // Reuse of an earlier layout (the engine side of incremental layout).
    //
    // A block formatting context root whose subtree did not change, laid out with the same inputs as last time, produces the same
    // fragments again: its children depend on nothing but the root's own frame (widths, heights, flags) and on the box subtree.
    // When the root is opened, the engine compares its frame with what it recorded at the end of the last layout. On a match it
    // copies the earlier children fragments, restores the few frame values the children would have left behind, and goes on to
    // close the box as usual. Subtrees that reach outside themselves (floats, out-of-flow positioning, columns, tables) are never
    // reused; see BoxTree hazards.
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
        public int Frag;
        public MemoKey Key;
        public double Cursor;
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

    private MemoEntry[] _mPrev = [];
    private MemoEntry[] _mCur = [];
    private int[] _logPrev = new int[64];
    private int[] _logCur = new int[64];
    private int _logPrevCount;
    private int _logCurCount;
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
            && ReferenceEquals(_memoOut, output) && _memoFontVersion == input.Fonts.Catalog.Version && _mPrev.Length >= boxes.Count * 2;
        _memoValid = false;
        if (_scratch is null || !ReferenceEquals(_scratch.Boxes, boxes))
        {
            _scratch = new FragmentTree(boxes);
            same = false;
        }

        int need = Math.Max(boxes.Count * 2, 2);
        if (_mPrev.Length < need)
        {
            _mPrev = new MemoEntry[need + need / 2];
            _mCur = new MemoEntry[_mPrev.Length];
            _logPrevCount = 0;
            _logCurCount = 0;
            same = false;
        }

        // The buffer written during this layout starts empty.
        for (int i = 0; i < _logCurCount; i++)
        {
            _mCur[_logCur[i]].Valid = false;
        }

        _logCurCount = 0;
        _reuse = same;
        _recording = true;
        _mcOpen = 0;
        _prevTree = same ? output : null;
        _hits = 0;
        _stores = 0;
        _out = _scratch;
        if (!same)
        {
            // Nothing from before is usable: forget the previous buffer too.
            for (int i = 0; i < _logPrevCount; i++)
            {
                _mPrev[_logPrev[i]].Valid = false;
            }

            _logPrevCount = 0;
        }
    }

    private void EndReuse(BoxTree boxes, in LayoutInput input, FragmentTree output)
    {
        output.SwapContents(_scratch!);
        (_mPrev, _mCur) = (_mCur, _mPrev);
        (_logPrev, _logCur) = (_logCur, _logPrev);
        (_logPrevCount, _logCurCount) = (_logCurCount, _logPrevCount);
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
            && c.Context is FormattingContext.Block or FormattingContext.FlowRoot
            && (nf.Flags & (FrameFlags.Multicol | FrameFlags.Flex | FrameFlags.Float)) == 0
            && _boxes.HazardOf(c.Index) == 0
            && _gridIntrinsicMode == 0
            && _sp < 50_000;
    }

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
        f.MemoHit = false;
        if (!_recording)
        {
            return false;
        }

        if (!MemoEligible(c, in f))
        {
            return false;
        }

        MemoKey key = KeyOf(in f);
        f.MemoKey = key;
        f.MemoRec = true;
        if (_prevTree is null || _boxes.IsDirty(c.Index))
        {
            return false;
        }

        int b = c.Index * 2;
        for (int w = 0; w < 2; w++)
        {
            ref MemoEntry e = ref _mPrev[b + w];
            if (!e.Valid || !e.Key.Equals(key))
            {
                continue;
            }

            FragmentTree prev = _prevTree;
            if (e.Frag >= prev.CountInternal || prev.BoxColumnOf(e.Frag) != c.Index)
            {
                continue;
            }

            var observer = new Remap(this);
            _out.CopyDescendants(prev, e.Frag, f.Frag, ref observer);
            f.Cursor = e.Cursor;
            f.FirstBaseline = e.FirstBaseline;
            f.OvL = e.OvL;
            f.OvT = e.OvT;
            f.OvR = e.OvR;
            f.OvB = e.OvB;
            f.Flags |= (FrameFlags)e.FlagsAdded;
            f.NextChild = -1;
            _pos = e.Pos;
            _neg = e.Neg;
            f.MemoHit = true;
            _hits++;
            return true;
        }

        return false;
    }

    private readonly struct Remap(BlockLayoutEngine engine) : IFragmentCopyObserver
    {
        public void Copied(int box, int sourceFragment, int newFragment) => engine.MemoCarry(box, sourceFragment, newFragment);
    }

    // A box inside a reused subtree keeps its record, pointing at the copy of its fragment.
    private void MemoCarry(int box, int sourceFragment, int newFragment)
    {
        int b = box * 2;
        for (int w = 0; w < 2; w++)
        {
            ref MemoEntry e = ref _mPrev[b + w];
            if (!e.Valid || e.Frag != sourceFragment)
            {
                continue;
            }

            MemoEntry copy = e;
            copy.Frag = newFragment;
            MemoPut(box, in copy);
            return;
        }
    }

    private void MemoPut(int box, in MemoEntry entry)
    {
        int b = box * 2;
        int way;
        if (_mCur[b].Valid && !_mCur[b].Key.Equals(entry.Key))
        {
            way = 1;
        }
        else
        {
            way = 0;
        }

        _mCur[b + way] = entry;
        _mCur[b + way].Valid = true;
        if (_logCurCount == _logCur.Length)
        {
            Array.Resize(ref _logCur, _logCur.Length * 2);
        }

        _logCur[_logCurCount++] = b + way;
    }

    // Called as a frame starts to close: its children are final, so what they left in the frame is what a reuse must restore.
    private void MemoClose(in Frame t)
    {
        var e = new MemoEntry
        {
            Frag = t.Frag,
            Key = t.MemoKey,
            Cursor = t.Cursor,
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

        while (_logCurCount > 0)
        {
            int slot = _logCur[_logCurCount - 1];
            if (_mCur[slot].Frag < fragmentCount)
            {
                break;
            }

            _mCur[slot].Valid = false;
            _logCurCount--;
        }
    }
}
