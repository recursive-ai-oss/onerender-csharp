namespace OneRender.Layout;

/// <summary>One border segment of the collapsing border model (CSS2 §17.6.2): the winner among the borders that meet on a grid line piece.</summary>
internal struct TableEdge
{
    /// <summary>The winning width in px (0 when nothing wins).</summary>
    public double Width;

    /// <summary>The box whose border won, or -1.</summary>
    public int Box;

    /// <summary>Which side of <see cref="Box"/> won: 0 top, 1 right, 2 bottom, 3 left.</summary>
    public byte Side;

    /// <summary>The winner's <c>border-style</c> as a <see cref="Style.BorderStyleKind"/>.</summary>
    public byte Style;

    /// <summary>The source's rank: 1 table, 2 column group, 3 column, 4 row group, 5 row, 6 cell; 0 for no border.</summary>
    public byte Prio;
}

internal struct TableCell
{
    public int Box;
    public int Row;
    public int Col;
    public int ColSpan;
    public int RowSpan;
    public int Limit;
    public double Fixed;
    public double Percent;
    public double Min;
    public double Max;
    public double Extra;
    public double Bt, Br, Bb, Bl;
    public double Width;
    public double Height;
    public double Baseline;
    public int Frag;
    public bool Skip;
    public TableEdge Et, Er, Eb, El;
}

internal struct TableRow
{
    public int Box;
    public int Group;
    public int FirstCell;
    public int CellCount;
    public double Spec;
    public bool Collapsed;
    public double Height;
    public double Y;
    public double Ascent;
    public int Frag;
}

internal struct TableGroup
{
    public int Box;
    public int FirstRow;
    public int RowCount;
    public int Frag;
    public bool Collapsed;
}

internal struct TableColumn
{
    public int Box;
    public int GroupBox;
    public double Fixed;
    public double Percent;
    public double Min;
    public double Max;
    public double Width;
    public double X;
    public bool Collapsed;
    public bool HasFixed;
    public int Frag;
    public int GroupFrag;
}

internal struct TableUnit
{
    public int Box;
    public bool IsGroup;
    public int Order;
}

/// <summary>The pooled working state of one table being analysed or laid out. One per nesting level, reused for every table at that level.</summary>
internal sealed class TableLevel
{
    public int BoxIndex;
    public bool Collapse;
    public double SpacingH;
    public double SpacingV;
    public int CellCount;
    public int RowCount;
    public int GroupCount;
    public int ColumnCount;
    public int CaptionCount;
    public int UnitCount;

    public TableCell[] Cells = new TableCell[16];
    public TableRow[] Rows = new TableRow[8];
    public TableGroup[] Groups = new TableGroup[4];
    public TableColumn[] Columns = new TableColumn[8];
    public TableUnit[] Units = new TableUnit[8];
    public int[] Captions = new int[2];
    public int[] Busy = new int[1000];
    public TableEdge[] Horizontal = [];
    public TableEdge[] Vertical = [];

    // The table's own border and padding as used (collapsing tables have half the outer collapsed borders and no padding).
    public double Bt, Br, Bb, Bl, Pt, Pr, Pb, Pl;
    public TableEdge Tt, Tr, Tb, Tl;

    // Column totals of the last analysis: the grid's content-box widths without caption influence.
    public double GridMin;
    public double GridMax;
    public bool LimitReported;

    public void Reset()
    {
        CellCount = RowCount = GroupCount = ColumnCount = CaptionCount = UnitCount = 0;
        LimitReported = false;
        Array.Clear(Busy);
    }

    public ref TableCell AddCell()
    {
        if (CellCount == Cells.Length)
        {
            Array.Resize(ref Cells, CellCount * 2);
        }

        ref TableCell c = ref Cells[CellCount++];
        c = default;
        c.Fixed = double.NaN;
        c.Percent = double.NaN;
        c.Frag = -1;
        return ref c;
    }

    public ref TableRow AddRow()
    {
        if (RowCount == Rows.Length)
        {
            Array.Resize(ref Rows, RowCount * 2);
        }

        ref TableRow r = ref Rows[RowCount++];
        r = default;
        r.Frag = -1;
        return ref r;
    }

    public ref TableGroup AddGroup()
    {
        if (GroupCount == Groups.Length)
        {
            Array.Resize(ref Groups, GroupCount * 2);
        }

        ref TableGroup g = ref Groups[GroupCount++];
        g = default;
        g.Frag = -1;
        return ref g;
    }

    public ref TableColumn AddColumn()
    {
        if (ColumnCount == Columns.Length)
        {
            Array.Resize(ref Columns, ColumnCount * 2);
        }

        ref TableColumn c = ref Columns[ColumnCount++];
        c = default;
        c.Box = -1;
        c.GroupBox = -1;
        c.Fixed = double.NaN;
        c.Percent = double.NaN;
        c.Frag = -1;
        c.GroupFrag = -1;
        return ref c;
    }

    public ref TableUnit AddUnit()
    {
        if (UnitCount == Units.Length)
        {
            Array.Resize(ref Units, UnitCount * 2);
        }

        return ref Units[UnitCount++];
    }

    public void AddCaption(int box)
    {
        if (CaptionCount == Captions.Length)
        {
            Array.Resize(ref Captions, CaptionCount * 2);
        }

        Captions[CaptionCount++] = box;
    }

    /// <summary>Makes room for the collapsed-border grid lines of the current rows and columns and clears them.</summary>
    public void PrepareEdges()
    {
        int h = (RowCount + 1) * ColumnCount, v = RowCount * (ColumnCount + 1);
        if (Horizontal.Length < h)
        {
            Horizontal = new TableEdge[Math.Max(h, Horizontal.Length * 2)];
        }

        if (Vertical.Length < v)
        {
            Vertical = new TableEdge[Math.Max(v, Vertical.Length * 2)];
        }

        Array.Clear(Horizontal, 0, h);
        Array.Clear(Vertical, 0, v);
    }
}
