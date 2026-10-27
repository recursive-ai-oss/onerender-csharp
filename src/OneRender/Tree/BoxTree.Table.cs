using OneRender.Style;

namespace OneRender.Tree;

public sealed partial class BoxTree
{
    // The role a box plays in the table model (CSS2 §17.2). 0 is anything else.
    private const int RoleCaption = 1;
    private const int RoleColumnGroup = 2;
    private const int RoleColumn = 3;
    private const int RoleRowGroup = 4;
    private const int RoleRow = 5;
    private const int RoleCell = 6;

    private int RoleOf(int b)
    {
        if ((BoxKind)_kind[b] != BoxKind.Container)
        {
            return 0;
        }

        return (FormattingContext)_ctx[b] switch
        {
            FormattingContext.TableCaption => RoleCaption,
            FormattingContext.TableColumnGroup => RoleColumnGroup,
            FormattingContext.TableColumn => RoleColumn,
            FormattingContext.TableRowGroup or FormattingContext.TableHeaderGroup or FormattingContext.TableFooterGroup => RoleRowGroup,
            FormattingContext.TableRow => RoleRow,
            FormattingContext.TableCell => RoleCell,
            _ => 0,
        };
    }

    /// <summary>
    /// CSS2 §17.2.1 stages 2 and 3 for a container that is itself a table part: children that are not proper table children are wrapped
    /// in anonymous boxes (rows in a table or row group, cells in a row). Columns and column groups drop what they must not contain.
    /// Wrapping happens iteratively, one level per call; the anonymous boxes are finished at once, so depth never follows the tree.
    /// </summary>
    private void FinishTablePart(int box)
    {
        var ctx = (FormattingContext)_ctx[box];
        if (ctx is FormattingContext.TableColumn)
        {
            // A column has no content.
            while (_first[box] >= 0)
            {
                Unlink(_first[box]);
            }

            _childLayout[box] = (byte)ChildLayout.Empty;
            return;
        }

        if (ctx is FormattingContext.TableColumnGroup)
        {
            for (int c = _first[box]; c >= 0;)
            {
                int next = _next[c];
                if (RoleOf(c) != RoleColumn)
                {
                    Unlink(c);
                }

                c = next;
            }

            _childLayout[box] = (byte)(_first[box] >= 0 ? ChildLayout.Block : ChildLayout.Empty);
            return;
        }

        FormattingContext wrapperCtx = ctx == FormattingContext.TableRow ? FormattingContext.TableCell : FormattingContext.TableRow;
        int properRole = ctx == FormattingContext.TableRow ? RoleCell : ctx == FormattingContext.Table ? -1 : RoleRow;
        int runHead = -1, runTail = -1;
        for (int c = _first[box]; c >= 0;)
        {
            int next = _next[c];
            int role = RoleOf(c);
            bool proper = ctx == FormattingContext.Table ? role is >= RoleCaption and <= RoleRow : role == properRole;
            if (IsNeutral(c))
            {
                if (runHead >= 0)
                {
                    runTail = c;
                }
            }
            else if (proper)
            {
                FlushTableRun(box, wrapperCtx, ref runHead, ref runTail);
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

        FlushTableRun(box, wrapperCtx, ref runHead, ref runTail);
        _childLayout[box] = (byte)(_first[box] >= 0 ? ChildLayout.Block : ChildLayout.Empty);
    }

    /// <summary>
    /// CSS2 §17.2.1 stage 3: table parts whose parent is not the table part they need (a cell outside a row, a row outside a table, ...)
    /// are wrapped, together with their consecutive siblings of the same kind, in an anonymous table. White space between them goes away.
    /// </summary>
    private void WrapStrayTableParts(int box)
    {
        int runHead = -1, runTail = -1;
        for (int c = _first[box]; c >= 0;)
        {
            int next = _next[c];
            if (RoleOf(c) != 0)
            {
                if (runHead < 0)
                {
                    runHead = c;
                }

                runTail = c;
            }
            else if (!IsNeutral(c) && !IsCollapsibleWhiteSpace(c))
            {
                WrapTableRun(box, ref runHead, ref runTail);
            }

            c = next;
        }

        WrapTableRun(box, ref runHead, ref runTail);
    }

    private void WrapTableRun(int parent, ref int head, ref int tail)
    {
        if (head >= 0)
        {
            int anon = WrapRunAs(parent, head, tail, FormattingContext.Table, new DisplayType(OuterDisplay.Block, InnerDisplay.Table));
            if (anon >= 0)
            {
                FinishContainer(anon);
            }
        }

        head = tail = -1;
    }

    private void FlushTableRun(int parent, FormattingContext ctx, ref int head, ref int tail)
    {
        if (head < 0)
        {
            return;
        }

        if (TrimRun(ref head, ref tail))
        {
            DisplayType display = ctx == FormattingContext.TableCell
                ? new DisplayType(OuterDisplay.TableInternal, InnerDisplay.TableCell)
                : new DisplayType(OuterDisplay.TableInternal, InnerDisplay.TableRow);
            int anon = WrapRunAs(parent, head, tail, ctx, display);
            if (anon >= 0)
            {
                bool block = false;
                for (int k = _first[anon]; k >= 0; k = _next[k])
                {
                    if (_level[k] == (byte)BoxLevel.Block && !IsNeutral(k))
                    {
                        block = true;
                        break;
                    }
                }

                if (block)
                {
                    _flags[anon] |= (ushort)BoxTraits.HasBlockChild;
                }

                FinishContainer(anon);
            }
        }

        head = tail = -1;
    }

    private int WrapRunAs(int parent, int head, int tail, FormattingContext ctx, DisplayType display)
    {
        StyleId style = Styled.Styles.GetAnonymous(new StyleId(_style[parent]), display);
        int anon = NewBox(BoxKind.Container, BoxLevel.Block, ctx, BoxTraits.Anonymous, style, -1, -1);
        if (anon < 0)
        {
            return -1;
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

        _childLayout[anon] = (byte)ChildLayout.Block;
        return anon;
    }
}
