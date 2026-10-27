using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

public sealed partial class Painter
{
    // CSS2 §17.6.2: a cell or table in the collapsing border model draws the borders that won the conflict on its sides, in the widths
    // layout gave it (half of each shared border). Returns false when the box has no such borders.
    private bool PaintTableBorders(int f, RectF rect)
    {
        if (!_tree.TryGetTableBorders(f, out TableBorderPaint p))
        {
            return false;
        }

        Insets b = _tree.BorderOf(f);
        PaintColor cur = PaintColor.Black;
        BorderSide top = TableSide(b.Top, p.StyleTop, p.ColorTop, cur);
        BorderSide right = TableSide(b.Right, p.StyleRight, p.ColorRight, cur);
        BorderSide bottom = TableSide(b.Bottom, p.StyleBottom, p.ColorBottom, cur);
        BorderSide left = TableSide(b.Left, p.StyleLeft, p.ColorLeft, cur);
        if (top.Width > 0 || right.Width > 0 || bottom.Width > 0 || left.Width > 0)
        {
            _list.DrawBorder(Id(f, 1), rect, default, top, right, bottom, left);
        }

        return true;
    }

    private BorderSide TableSide(double width, BorderStyleKind style, ColorValue color, PaintColor current) =>
        new(SnapWidth(SideWidth(width, style)), MapStyle(style), ToColor(color, current));
}
