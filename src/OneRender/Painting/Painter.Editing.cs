using System.Numerics;
using OneRender.Forms;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

// Editing overlay (HTML §4.10.5.1 text fields, §4.10.11 textarea; CSS UI 4 §5 caret-color; CSS Pseudo 4 §3.2 ::selection): the selection
// highlight under the control's text, the underline of an IME preedit, the caret on top, and the clip and horizontal scroll of a
// single-line field. Only the control named by PaintOptions.Editing is touched; everything else paints as before.
public sealed partial class Painter
{
    private const int EditPart = 10;

    private TextEditView? _edit;
    private StyledNode _editControl;
    private StyledNode _editTextNode;
    private int _editControlBox = -1;
    private int _editTextBox = -1;
    private bool _recolor;
    private PaintColor _recolorColor;
    private ItemId _recolorId;

    private void ResolveEditing(in PaintOptions options)
    {
        _edit = null;
        _editControlBox = -1;
        _editTextBox = -1;
        TextEditView? view = options.Editing;
        if (view is null)
        {
            return;
        }

        StyledTree styled = _boxes.Styled;
        StyledNode control = view.SourceElement >= 0 ? styled.FindBySource(view.SourceElement) : view.ControlNode;
        if (!control.IsValid || !ReferenceEquals(control.Tree, styled))
        {
            return;
        }

        StyledNode text = default;
        for (StyledNode c = control.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == StyledNodeKind.Text)
            {
                text = c;
                break;
            }
        }

        if (text.IsNull)
        {
            return;
        }

        Box cb = _boxes.PrincipalBox(control);
        if (cb.IsNull)
        {
            return;
        }

        _edit = view;
        _editControl = control;
        _editTextNode = text;
        _editControlBox = cb.Index;
        Box tb = _boxes.PrincipalBox(text);
        _editTextBox = tb.IsNull ? -1 : tb.Index;
    }

    private TextGeometry EditGeometry(double lineHeight) => new(_tree, _editControl, _editTextNode, lineHeight, _edit!.Multiline);

    private void PaintText(int f)
    {
        if (_edit is null || _editTextBox < 0 || _tree.BoxOf(f) != _editTextBox || _recolor)
        {
            PaintTextCore(f);
            return;
        }

        PaintEditedText(f);
    }

    private void PaintBoxDecoration(int f)
    {
        PaintBoxDecorationCore(f);
        if (_edit is not null && _tree.BoxOf(f) == _editControlBox && _edit.CaretVisible && _editTextNode.Text.IsEmpty)
        {
            PaintEmptyCaret(f);
        }
    }

    // An empty field has no text fragment to hang the caret on: it sits at the start of the content box.
    private void PaintEmptyCaret(int f)
    {
        TextEditView e = _edit!;
        Box box = _boxes.Make(_tree.BoxOf(f));
        TextGeometry g = EditGeometry(e.LineHeight > 0 ? e.LineHeight : box.Values.FontSize * 1.15);
        if (g.TryCaret(0, out double x, out double y, out double h))
        {
            FillCaret(f, x, y, h, in box.Values);
        }
    }

    private void FillCaret(int f, double x, double y, double h, in ComputedValues v)
    {
        PaintColor c = _edit!.CaretColor;
        if (c.IsTransparent)
        {
            c = new PaintColor(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        }

        RectF r = SnapRect(x, y, 1, h);
        if (r.Width < 1f)
        {
            r = new RectF(r.X, r.Y, 1f, r.Height);
        }

        _list.FillRect(ControlId(f, EditPart + 3), r, c);
    }

    private void PaintEditedText(int f)
    {
        TextEditView e = _edit!;
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        TextGeometry g = EditGeometry(e.LineHeight > 0 ? e.LineHeight : v.FontSize * 1.15);
        bool clipped = false;
        if (!e.Multiline && g.TryContentRect(out Rect content))
        {
            // The field shows a window onto its text that follows the caret (HTML §15.5.1: the inner editor scrolls horizontally).
            double sx = e.ScrollX;
            if (g.TryCaret(e.CaretOffset, out double cx, out _, out _) && g.TryCaret(_editTextNode.Text.Length, out double endX, out _, out _))
            {
                double local = cx - content.X, total = endX - content.X, w = content.Width;
                if (local - sx > w - 1)
                {
                    sx = local - w + 1;
                }

                if (local < sx)
                {
                    sx = local;
                }

                sx = Math.Clamp(sx, 0, Math.Max(0, total - w + 1));
                e.ScrollX = sx;
            }

            if (_scopes + 2 < MaxScopes)
            {
                _list.PushClipRect(ControlId(f, EditPart), SnapRect(content.X, content.Y, content.Width, content.Height));
                _list.PushTransform(ControlId(f, EditPart + 1), Matrix3x2.CreateTranslation(-(float)sx, 0));
                _scopes += 2;
                clipped = true;
            }
        }

        Rect bb = _tree.AbsoluteBorderBox(_tree.Make(f));
        int start = _tree.TextStartOf(f), end = start + _tree.TextLengthOf(f);
        int ss = Math.Max(e.SelectionStart, start), se = Math.Min(e.SelectionEnd, end);
        bool hasSel = se > ss && e.SelectionEnd > e.SelectionStart;
        RectF selRect = default;
        if (hasSel)
        {
            double x0 = bb.X + g.LocalX(f, ss), x1 = bb.X + g.LocalX(f, se);
            selRect = SnapRect(Math.Min(x0, x1), bb.Y, Math.Abs(x1 - x0), bb.Height);
            _list.FillRect(ControlId(f, EditPart + 2), selRect, e.SelectionBackground);
        }

        PaintTextCore(f);
        if (hasSel && !e.SelectionForeground.IsTransparent)
        {
            // CSS Pseudo 4 §3.2: ::selection can change the text color; the selected glyphs are drawn again, clipped to the highlight.
            _list.PushClipRect(ControlId(f, EditPart + 4), selRect);
            _list.FillRect(ControlId(f, EditPart + 5), selRect, e.SelectionBackground);
            _recolor = true;
            _recolorColor = e.SelectionForeground;
            _recolorId = ControlId(f, EditPart + 6);
            PaintTextCore(f);
            _recolor = false;
            _list.PopClip();
        }

        if (e.CompositionStart >= 0)
        {
            int cs = Math.Max(e.CompositionStart, start), ce = Math.Min(e.CompositionEnd, end);
            if (ce > cs)
            {
                PaintColor c = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
                double x0 = bb.X + g.LocalX(f, cs), x1 = bb.X + g.LocalX(f, ce);
                double y = _baseY[f] + 1.5;
                _list.FillRect(ControlId(f, EditPart + 7), SnapRect(Math.Min(x0, x1), y, Math.Abs(x1 - x0), 1), c);
                int ts = Math.Max(e.TargetStart, cs), te = Math.Min(e.TargetEnd, ce);
                if (te > ts)
                {
                    double t0 = bb.X + g.LocalX(f, ts), t1 = bb.X + g.LocalX(f, te);
                    _list.FillRect(ControlId(f, EditPart + 8), SnapRect(Math.Min(t0, t1), y, Math.Abs(t1 - t0), 2), c);
                }
            }
        }

        if (e.CaretVisible && g.NextFragment(f) < 0 && g.TryCaret(e.CaretOffset, out double x, out double y2, out double h))
        {
            FillCaret(f, x, y2, h, in v);
        }

        if (clipped)
        {
            _list.PopTransform();
            _list.PopClip();
            _scopes -= 2;
        }
    }
}
