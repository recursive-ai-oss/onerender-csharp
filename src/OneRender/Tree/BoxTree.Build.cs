using OneRender.Style;

namespace OneRender.Tree;

public sealed partial class BoxTree
{
    /// <summary>
    /// The deepest chain of inline boxes the engine splits around a block-level descendant. Splitting costs one new box per
    /// inline ancestor, so an unbounded chain with many block children would be quadratic; deeper blocks are laid out as
    /// inline-blocks instead and a diagnostic says so.
    /// </summary>
    public const int MaxSplitDepth = 64;

    private BFrame[] _frames = new BFrame[64];
    private int _top;

    /// <summary>
    /// Generates the boxes for the whole styled tree, replacing any earlier boxes (CSS Display 3 §2, CSS2 §9.2).
    /// </summary>
    /// <param name="cancellationToken">Checked every few hundred nodes.</param>
    /// <exception cref="OperationCanceledException">Cancelled; the tree is partly built.</exception>
    public void Build(CancellationToken cancellationToken = default)
    {
        StyledTree st = Styled;
        _count = 0;
        _limitReported = false;
        if (_nodeBox.Length < st.Count)
        {
            _nodeBox = new int[Math.Max(st.Count, _nodeBox.Length * 2)];
        }

        Array.Fill(_nodeBox, -1);
        _top = -1;
        if (st.Count == 0)
        {
            PrepareIncremental();
            return;
        }

        Visit(0);
        int counter = 0;
        while (_top >= 0)
        {
            if ((++counter & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int child = _frames[_top].Next;
            if (child < 0)
            {
                Pop();
                continue;
            }

            _frames[_top].Next = st.NextSiblingOf(child);
            Visit(child);
        }

        PrepareIncremental();
    }

    private void Visit(int s)
    {
        StyledTree st = Styled;
        var kind = (StyledNodeKind)st.KindOf(s);
        StyleId sid = st.StyleOf(s);
        int parentFrame = _top;
        int parentBox = parentFrame < 0 ? -1 : _frames[_frames[parentFrame].Attach].Box;
        switch (kind)
        {
            case StyledNodeKind.Text:
                if (parentBox >= 0 && !st.TextOf(s).IsEmpty)
                {
                    int t = NewBox(BoxKind.Text, BoxLevel.Inline, FormattingContext.None, BoxTraits.None, sid, s, parentBox);
                    if (t >= 0)
                    {
                        _nodeBox[s] = t;
                    }
                }

                return;
            case StyledNodeKind.Marker:
                AddMarker(s, parentBox, sid);
                return;
        }

        ref readonly ComputedValues v = ref st.Styles[sid];
        DisplayType d = v.Display;
        if (d.IsNone)
        {
            return;
        }

        if (d.IsContents && s == 0)
        {
            // The root has no parent box to promote its children into, and a tree needs a root box: lay it out as a block.
            d = DisplayType.Block;
        }

        ReplacedKind replaced = st.ReplacedOf(s);
        bool isBr = kind == StyledNodeKind.Element && replaced == ReplacedKind.None && st.TagNameOf(s).Equals("br", StringComparison.Ordinal);
        if (d.IsContents)
        {
            // CSS Display 3 §2.5: the element generates no box and its children are generated as if they were its parent's children;
            // replaced elements and br have no children to promote, so they generate nothing.
            if (replaced == ReplacedKind.None && !isBr && kind == StyledNodeKind.Element)
            {
                PushFrame(s, st.FirstChildOf(s), -1, -1);
            }

            return;
        }

        bool outOfFlow = v.IsOutOfFlow;
        BoxTraits flags = outOfFlow ? BoxTraits.OutOfFlow : BoxTraits.None;
        if (d.IsListItem)
        {
            flags |= BoxTraits.ListItem;
        }

        // CSS Display 3 §2.7: children of flex and grid containers are blockified.
        if (parentBox >= 0 && (FormattingContext)_ctx[parentBox] is FormattingContext.Flex or FormattingContext.Grid)
        {
            DisplayType blockified = d.Blockified();
            if (blockified != d)
            {
                d = blockified;
                flags |= BoxTraits.Blockified;
            }
        }

        BoxKind bk;
        BoxLevel level;
        FormattingContext ctx;
        if (isBr)
        {
            bk = BoxKind.LineBreak;
            level = BoxLevel.Inline;
            ctx = FormattingContext.None;
        }
        else if (replaced != ReplacedKind.None)
        {
            bk = BoxKind.Replaced;
            level = d.IsBlockLevel ? BoxLevel.Block : BoxLevel.Inline;
            ctx = FormattingContext.None;
        }
        else
        {
            Classify(d, out bk, out level, out ctx);
        }

        // CSS2 §9.2.1.1: a block-level box in an inline box splits the inline box around it. Out-of-flow boxes do not split it.
        int placeParent = parentBox;
        int splitBase = -1;
        if (parentBox >= 0 && level == BoxLevel.Block && !outOfFlow && (BoxKind)_kind[parentBox] == BoxKind.Inline)
        {
            int fc = FindContainerFrame(parentFrame);
            if (fc >= 0)
            {
                placeParent = _frames[fc].Box;
                splitBase = fc + 1;
            }
            else
            {
                level = BoxLevel.Inline;
                if (bk == BoxKind.Container)
                {
                    ctx = ctx == FormattingContext.Block ? FormattingContext.FlowRoot : ctx;
                }

                flags |= BoxTraits.UnsplitBlockInInline;
                Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "block-level box inside more than " + MaxSplitDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " nested inline boxes is laid out as an inline-block");
            }
        }

        int b = NewBox(bk, level, ctx, flags, sid, s, placeParent);
        if (b < 0)
        {
            return;
        }

        if (level == BoxLevel.Block && !outOfFlow && placeParent >= 0 && (BoxKind)_kind[placeParent] == BoxKind.Container)
        {
            _flags[placeParent] |= (ushort)BoxTraits.HasBlockChild;
        }

        _nodeBox[s] = b;
        if (bk is BoxKind.Container or BoxKind.Inline)
        {
            PushFrame(s, kind == StyledNodeKind.Element ? st.FirstChildOf(s) : -1, b, splitBase);
            if (kind != StyledNodeKind.Element && !st.TextOf(s).IsEmpty)
            {
                // The generated string of ::before / ::after is the box's only child.
                NewBox(BoxKind.Text, BoxLevel.Inline, FormattingContext.None, BoxTraits.None, sid, s, b);
            }
        }
    }

    private static void Classify(DisplayType d, out BoxKind kind, out BoxLevel level, out FormattingContext ctx)
    {
        switch (d.Outside)
        {
            case OuterDisplay.Block:
                kind = BoxKind.Container;
                level = BoxLevel.Block;
                ctx = d.Inside switch
                {
                    InnerDisplay.FlowRoot => FormattingContext.FlowRoot,
                    InnerDisplay.Table => FormattingContext.Table,
                    InnerDisplay.Flex => FormattingContext.Flex,
                    InnerDisplay.Grid => FormattingContext.Grid,
                    _ => FormattingContext.Block,
                };
                return;
            case OuterDisplay.TableInternal:
                kind = BoxKind.Container;
                level = BoxLevel.Block;
                ctx = d.Inside switch
                {
                    InnerDisplay.TableRowGroup => FormattingContext.TableRowGroup,
                    InnerDisplay.TableHeaderGroup => FormattingContext.TableHeaderGroup,
                    InnerDisplay.TableFooterGroup => FormattingContext.TableFooterGroup,
                    InnerDisplay.TableRow => FormattingContext.TableRow,
                    InnerDisplay.TableCell => FormattingContext.TableCell,
                    InnerDisplay.TableColumnGroup => FormattingContext.TableColumnGroup,
                    InnerDisplay.TableColumn => FormattingContext.TableColumn,
                    _ => FormattingContext.TableCaption,
                };
                return;
            default:
                level = BoxLevel.Inline;
                if (d.Inside is InnerDisplay.Flow or InnerDisplay.Ruby)
                {
                    kind = BoxKind.Inline;
                    ctx = FormattingContext.None;
                    return;
                }

                kind = BoxKind.Container;
                ctx = d.Inside switch
                {
                    InnerDisplay.Table => FormattingContext.Table,
                    InnerDisplay.Flex => FormattingContext.Flex,
                    InnerDisplay.Grid => FormattingContext.Grid,
                    _ => FormattingContext.FlowRoot,
                };
                return;
        }
    }

    private void AddMarker(int s, int parentBox, StyleId sid)
    {
        if (parentBox < 0)
        {
            return;
        }

        StyleTable styles = Styled.Styles;
        ref readonly ComputedValues mv = ref styles[sid];
        if (!mv.ListStyleTypeIsString && mv.ListStyleType.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return; // CSS Lists 3 §3: list-style-type: none generates no marker (unless content gives it one, which comes with generated content)
        }

        bool outside = styles[new StyleId(_style[parentBox])].ListStylePosition == ListStylePositionKind.Outside;
        int m = NewBox(BoxKind.Marker, BoxLevel.Inline, FormattingContext.None, outside ? BoxTraits.OutsideMarker : BoxTraits.None, sid, s, parentBox);
        if (m >= 0)
        {
            _nodeBox[s] = m;
        }
    }

    /// <summary>Finds the frame of the block container a chain of inline frames hangs from; -1 when the chain is longer than the limit.</summary>
    private int FindContainerFrame(int parentFrame)
    {
        int f = _frames[parentFrame].Attach;
        int depth = 0;
        while ((BoxKind)_kind[_frames[f].Box] == BoxKind.Inline)
        {
            if (++depth > MaxSplitDepth)
            {
                return -1;
            }

            f = _frames[f - 1].Attach;
        }

        return f;
    }

    private void PushFrame(int node, int next, int box, int splitBase)
    {
        if (_top + 1 == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        int index = _top + 1;
        _frames[index] = new BFrame(node, next, box, box >= 0 ? index : _frames[_top].Attach, splitBase);
        _top = index;
    }

    private void Pop()
    {
        BFrame f = _frames[_top];
        if (f.Box >= 0)
        {
            if ((BoxKind)_kind[f.Box] == BoxKind.Container)
            {
                FinishContainer(f.Box);
            }
            else
            {
                _childLayout[f.Box] = (byte)(_first[f.Box] >= 0 ? ChildLayout.Inline : ChildLayout.Empty);
            }
        }

        if (f.SplitBase >= 0)
        {
            MakeContinuations(f.SplitBase, _top);
        }

        _top--;
    }

    /// <summary>
    /// After a block-level box that split the inline boxes of frames <paramref name="splitBase"/> up to <paramref name="blockFrame"/>
    /// (exclusive) has been generated, open a continuation of each inline box after it, nested like the originals (CSS2 §9.2.1.1).
    /// </summary>
    private void MakeContinuations(int splitBase, int blockFrame)
    {
        int containerBox = _frames[splitBase - 1].Box;
        int last = -1;
        for (int i = splitBase; i < blockFrame; i++)
        {
            int old = _frames[i].Box;
            if (old < 0)
            {
                continue;
            }

            BoxTraits flags = (BoxTraits)_flags[old] & ~BoxTraits.ListItem | BoxTraits.Continuation;
            int c = NewBox(BoxKind.Inline, (BoxLevel)_level[old], FormattingContext.None, flags, new StyleId(_style[old]), _node[old], last >= 0 ? last : containerBox);
            if (c < 0)
            {
                return;
            }

            _nextPiece[old] = c;
            _frames[i].Box = c;
            last = c;
        }
    }

    // ---- finishing a container: anonymous block wrapping and white space (CSS2 §9.2.1.1, CSS Text 3 §4.1.1) ----

    private void FinishContainer(int box)
    {
        switch ((FormattingContext)_ctx[box])
        {
            case FormattingContext.Block:
            case FormattingContext.FlowRoot:
            case FormattingContext.TableCell:
            case FormattingContext.TableCaption:
                WrapStrayTableParts(box);
                FinishFlow(box);
                break;
            case FormattingContext.Table:
            case FormattingContext.TableRowGroup:
            case FormattingContext.TableHeaderGroup:
            case FormattingContext.TableFooterGroup:
            case FormattingContext.TableRow:
            case FormattingContext.TableColumnGroup:
            case FormattingContext.TableColumn:
                FinishTablePart(box);
                break;
            case FormattingContext.Flex:
            case FormattingContext.Grid:
                FinishItems(box);
                break;
            default:
                // Table structure and ruby: only the white space between children goes; the table support adds the rest.
                for (int c = _first[box]; c >= 0;)
                {
                    int next = _next[c];
                    if (IsCollapsibleWhiteSpace(c))
                    {
                        Unlink(c);
                    }

                    c = next;
                }

                _childLayout[box] = (byte)(_first[box] >= 0 ? ChildLayout.Block : ChildLayout.Empty);
                break;
        }
    }

    private void FinishFlow(int box)
    {
        if (((BoxTraits)_flags[box] & BoxTraits.HasBlockChild) == 0)
        {
            // An inline formatting context (or empty): trim the white space that would collapse away at the line edges.
            if (_first[box] >= 0)
            {
                int head = _first[box], tail = _last[box];
                TrimRun(ref head, ref tail);
            }

            bool any = false, inline = false;
            for (int c = _first[box]; c >= 0; c = _next[c])
            {
                any = true;
                if (!IsNeutral(c))
                {
                    inline = true;
                    break;
                }
            }

            _childLayout[box] = (byte)(!any ? ChildLayout.Empty : inline ? ChildLayout.Inline : ChildLayout.Block);
            return;
        }

        // CSS2 §9.2.1.1: when a block container has block-level children, wrap each run of inline-level children in an anonymous block.
        int runHead = -1, runTail = -1;
        for (int c = _first[box]; c >= 0;)
        {
            int next = _next[c];
            if (IsNeutral(c))
            {
                if (runHead >= 0)
                {
                    runTail = c;
                }
            }
            else if (_level[c] == (byte)BoxLevel.Block)
            {
                FlushRun(box, ref runHead, ref runTail);
            }
            else
            {
                if (runHead < 0)
                {
                    runHead = c;
                }

                runTail = c;
            }

            c = next;
        }

        FlushRun(box, ref runHead, ref runTail);
        _childLayout[box] = (byte)ChildLayout.Block;
    }

    private void FlushRun(int parent, ref int head, ref int tail)
    {
        if (head < 0)
        {
            return;
        }

        if (TrimRun(ref head, ref tail))
        {
            WrapRun(parent, head, tail);
        }

        head = -1;
        tail = -1;
    }

    /// <summary>
    /// Removes white-space-only text that would collapse away at the edges of a run of inline-level siblings and reports
    /// whether in-flow inline content remains. <paramref name="head"/> and <paramref name="tail"/> are updated.
    /// </summary>
    private bool TrimRun(ref int head, ref int tail)
    {
        int parent = _parent[head];
        int before = _prev[head];
        int after = _next[tail];
        for (int c = head; c != after && c >= 0;)
        {
            int next = _next[c];
            if (IsNeutral(c))
            {
                c = next;
            }
            else if (IsCollapsibleWhiteSpace(c))
            {
                Unlink(c);
                c = next;
            }
            else
            {
                break;
            }
        }

        int cur = after >= 0 ? _prev[after] : _last[parent];
        while (cur >= 0 && cur != before)
        {
            int prev = _prev[cur];
            if (IsNeutral(cur))
            {
                cur = prev;
            }
            else if (IsCollapsibleWhiteSpace(cur))
            {
                Unlink(cur);
                cur = prev;
            }
            else
            {
                break;
            }
        }

        head = before >= 0 ? _next[before] : _first[parent];
        tail = after >= 0 ? _prev[after] : _last[parent];
        if (head < 0 || head == after)
        {
            head = tail = -1; // trimming emptied the run
            return false;
        }

        for (int c = head; c >= 0; c = c == tail ? -1 : _next[c])
        {
            if (!IsNeutral(c))
            {
                return true;
            }
        }

        return false;
    }

    private void WrapRun(int parent, int head, int tail)
    {
        StyleId style = Styled.Styles.GetAnonymous(new StyleId(_style[parent]), DisplayType.Block);
        int anon = NewBox(BoxKind.Container, BoxLevel.Block, FormattingContext.Block, BoxTraits.Anonymous, style, -1, -1);
        if (anon < 0)
        {
            return;
        }

        int before = _prev[head];
        int after = _next[tail];
        _parent[anon] = parent;
        _prev[anon] = before;
        _next[anon] = after;
        if (before >= 0)
        {
            _next[before] = anon;
        }
        else
        {
            _first[parent] = anon;
        }

        if (after >= 0)
        {
            _prev[after] = anon;
        }
        else
        {
            _last[parent] = anon;
        }

        _prev[head] = -1;
        _next[tail] = -1;
        _first[anon] = head;
        _last[anon] = tail;
        for (int c = head; c >= 0; c = _next[c])
        {
            _parent[c] = anon;
        }

        _childLayout[anon] = (byte)ChildLayout.Inline;
    }

    private void FinishItems(int box)
    {
        // Flexbox §4 and Grid §6: each contiguous run of text in a flex or grid container becomes an anonymous block item;
        // a run of only collapsible white space is not rendered.
        int runHead = -1, runTail = -1;
        for (int c = _first[box]; c >= 0;)
        {
            int next = _next[c];
            if ((BoxKind)_kind[c] == BoxKind.Text)
            {
                if (IsCollapsibleWhiteSpace(c))
                {
                    Unlink(c);
                }
                else
                {
                    if (runHead < 0)
                    {
                        runHead = c;
                    }

                    runTail = c;
                }
            }
            else if (runHead >= 0)
            {
                WrapRun(box, runHead, runTail);
                runHead = runTail = -1;
            }

            c = next;
        }

        if (runHead >= 0)
        {
            WrapRun(box, runHead, runTail);
        }

        _childLayout[box] = (byte)(_first[box] >= 0 ? ChildLayout.Block : ChildLayout.Empty);
    }

    /// <summary>Out-of-flow boxes and outside markers do not take part in the lines or block stack of their parent.</summary>
    private bool IsNeutral(int b) => ((BoxTraits)_flags[b] & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) != 0;

    /// <summary>
    /// A text box of only document white space that the <c>white-space</c> of its style collapses away (CSS Text 3 §4.1.1):
    /// not under <c>preserve</c>, <c>preserve-spaces</c> or <c>break-spaces</c>, and not under <c>preserve-breaks</c> when it has a line break.
    /// </summary>
    private bool IsCollapsibleWhiteSpace(int b)
    {
        if ((BoxKind)_kind[b] != BoxKind.Text)
        {
            return false;
        }

        WhiteSpaceCollapseKind mode = Styled.Styles[new StyleId(_style[b])].WhiteSpaceCollapse;
        if (mode is WhiteSpaceCollapseKind.Preserve or WhiteSpaceCollapseKind.PreserveSpaces or WhiteSpaceCollapseKind.BreakSpaces)
        {
            return false;
        }

        foreach (char ch in Styled.TextOf(_node[b]))
        {
            if (ch is '\n' or '\r')
            {
                if (mode == WhiteSpaceCollapseKind.PreserveBreaks)
                {
                    return false;
                }
            }
            else if (ch is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private struct BFrame(int node, int next, int box, int attach, int splitBase)
    {
        public int Node = node;
        public int Next = next;
        public int Box = box;
        public int Attach = attach;
        public int SplitBase = splitBase;
    }
}
