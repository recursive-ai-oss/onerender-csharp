using OneRender.Forms;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

// Text editing in text fields and textareas (HTML §4.10.5.1, §4.10.11, §4.10.18.6; UI Events; Input Events Level 2). The page owns one
// TextEditSession per edited control (kept in the form state so it survives restyles); the focused control's session is the active one.
// Keys, pointer presses, host text input, clipboard commands and IME composition all go through the session, which calls back into
// PageEditHost to fire events and put the new text on screen.
public sealed partial class InteractivePage
{
    private TextEditSession? _edit;
    private Node _editElement;
    private PageEditHost? _editHost;
    private InputEvent _ie = null!;
    private CompositionEvent _cev = null!;
    private DomEvent _clipEvent = null!;
    private InputOutcome _editOutcome;
    private CancellationToken _editToken;
    private bool _dragEdit;
    private double _dragDx, _dragDy, _dragScroll0;

    /// <summary>Where Ctrl/Cmd+C, X and V and <see cref="Copy"/>, <see cref="Cut"/> and <see cref="Paste"/> read and write text. Null (the default) makes copy do nothing and paste insert nothing.</summary>
    public ITextClipboard? Clipboard { get; set; }

    private sealed class PageEditHost(InteractivePage page) : EditHost
    {
        public override TextGeometry Geometry => page.EditGeometry();

        public override ITextClipboard? Clipboard => page.Clipboard;

        public override bool BeforeInput(string inputType, string? data, bool composing, double time)
        {
            InputEvent e = page._ie;
            e.Reset("beforeinput", true, !composing, time);
            e.Detail = 0;
            e.InputType = inputType;
            e.Data = data;
            e.IsComposing = composing;
            return page._events.Dispatch(e, EventTarget.Of(page._editElement)) || composing;
        }

        public override void Input(string inputType, string? data, bool composing, double time)
        {
            InputEvent e = page._ie;
            e.Reset("input", true, false, time);
            e.Detail = 0;
            e.InputType = inputType;
            e.Data = data;
            e.IsComposing = composing;
            page._events.Dispatch(e, EventTarget.Of(page._editElement));
        }

        public override void SelectionChanged(bool select, double time)
        {
            page._editOutcome |= InputOutcome.EditChanged;
            if (select)
            {
                page.FireSimple("select", true, false, page._editElement, time);
            }

            page.FireSimple("selectionchange", true, false, page._editElement, time);
        }

        public override void Composition(string type, string data, double time)
        {
            CompositionEvent e = page._cev;
            e.Reset(type, true, false, time);
            e.Detail = 0;
            e.Data = data;
            page._events.Dispatch(e, EventTarget.Of(page._editElement));
        }

        public override bool ClipboardEvent(string type, double time)
        {
            DomEvent e = page._clipEvent;
            e.Reset(type, true, true, time);
            return page._events.Dispatch(e, EventTarget.Of(page._editElement));
        }

        public override void Sync()
        {
            if (!page._page.ApplyTextEdit(page._editElement, page._edit!, page._editToken))
            {
                page.Rebuild(page._editToken);
            }
            else
            {
                page._hits.Rebuild();
                page._generation++;
            }

            page._editOutcome |= InputOutcome.Relaid | InputOutcome.EditChanged;
        }
    }

    private void InitEditing()
    {
        _ie = new InputEvent("input");
        _cev = new CompositionEvent("compositionstart");
        _clipEvent = new DomEvent("copy", true, true);
        _editHost = new PageEditHost(this);
    }

    // ------------------------------------------------------------------ sessions

    private static bool IsEditableText(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        ControlKind k = HtmlFrontEnd.Classify(n);
        return k is ControlKind.TextField or ControlKind.Password or ControlKind.TextArea;
    }

    private TextEditSession? SessionFor(Node element, bool create)
    {
        if (!IsEditableText(element))
        {
            return null;
        }

        if (_page.Form.TryGetText(element, out TextEditSession? s))
        {
            return s;
        }

        if (!create)
        {
            return null;
        }

        ControlKind kind = HtmlFrontEnd.Classify(element);
        s = new TextEditSession(kind);
        s.Editor.SetValue(HtmlFrontEnd.InitialValue(element, kind));
        s.Initial = s.Editor.Text.ToString();
        s.Editor.Select(0, 0);
        s.Placeholder = HtmlFrontEnd.PlaceholderOf(element, kind);
        s.RebuildDisplay();
        _page.Form.SetText(element, s);
        return s;
    }

    // Attributes that decide what the control accepts, read when editing starts and again for each session use.
    private void ReadControlAttributes(Node element, TextEditSession s)
    {
        s.ReadOnly = FocusRules.HasAttribute(element, "readonly");
        s.Disabled = IsDisabledControl(element);
        string? type = DocumentSnapshots.Attr(element, "type");
        s.Numeric = type is not null && type.Equals("number", StringComparison.OrdinalIgnoreCase);
        string? max = DocumentSnapshots.Attr(element, "maxlength");
        s.Editor.MaxLength = !s.Numeric && max is not null && int.TryParse(max, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int m) ? m : -1;
        TextEditView v = s.View;
        v.SourceElement = element.Index;
        StyledNode n = _page.Styled.FindBySource(element.Index);
        if (!n.IsNull)
        {
            int box = _page.Boxes.PrincipalBox(n).Index;
            if (box >= 0)
            {
                ref readonly ComputedValues cv = ref _page.Boxes.Make(box).Values;
                v.LineHeight = Forms.FormMetrics.LineSpacing(_page.Options.Fonts, in cv, withGap: false);
                ColorValue r = cv.CaretColor.Resolve(cv.Color);
                v.CaretColor = cv.CaretColorAuto ? default : new Painting.PaintColor(r.R, r.G, r.B, r.A);
            }
        }
    }

    private TextEditSession? Active()
    {
        if (_edit is null || _focused.IsNull || !_focused.Equals(_editElement))
        {
            return null;
        }

        return _edit;
    }

    private void BeginEditing(Node target, FocusReason reason, double time)
    {
        TextEditSession? s = SessionFor(target, create: true);
        if (s is null)
        {
            return;
        }

        _edit = s;
        _editElement = target;
        ReadControlAttributes(target, s);
        s.ValueAtFocus = s.Editor.Text.ToString();
        if (reason == FocusReason.Keyboard && !s.Multiline)
        {
            s.Editor.Select(0, s.Editor.Length);
        }

        s.Touch(time);
        s.UpdateView(time);
        _page.Form.ActiveView = s.View;
    }

    private void EndEditing(Node old, double time)
    {
        if (_edit is null || !old.Equals(_editElement))
        {
            return;
        }

        TextEditSession s = _edit;
        if (s.Composing)
        {
            _editToken = default;
            s.CompositionFinish(_editHost!, s.Display.Slice(s.ReplaceStart, s.PreeditLength).ToString(), time);
        }

        FireChangeIfNeeded(s, time);
        _page.Form.ActiveView = null;
        s.View.CaretVisible = false;
        _edit = null;
        _editElement = default;
        _dragEdit = false;
    }

    private void FireChangeIfNeeded(TextEditSession s, double time)
    {
        if (s.ValueAtFocus is null || s.Editor.Text.SequenceEqual(s.ValueAtFocus))
        {
            return;
        }

        s.ValueAtFocus = s.Editor.Text.ToString();
        FireSimple("change", true, false, _editElement, time);
    }

    private TextGeometry EditGeometry()
    {
        StyledNode control = _page.Styled.FindBySource(_editElement.Index);
        if (control.IsNull || _edit is null)
        {
            return default;
        }

        return new TextGeometry(_page.Fragments, control, TextControls.ValueNode(control), _edit.View.LineHeight, _edit.Multiline);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>The value of a text field or textarea now: what the user typed, else the <c>value</c> attribute (or the textarea's text).</summary>
    /// <param name="control">An <c>input</c> (text, password, email, number, search, url, tel) or <c>textarea</c>.</param>
    /// <returns>The value, with line breaks as <c>\n</c>; empty for anything else.</returns>
    public string GetValue(Node control)
    {
        TextEditSession? s = SessionFor(control, create: false);
        if (s is not null)
        {
            return s.Editor.Text.ToString();
        }

        return IsEditableText(control) ? HtmlFrontEnd.InitialValue(control, HtmlFrontEnd.Classify(control)) : string.Empty;
    }

    /// <summary>Sets the value of a text control as a script does: no events, the undo history is dropped, the caret goes to the end.</summary>
    /// <param name="control">A text field or textarea.</param>
    /// <param name="value">The new value (cut at the control's <c>maxlength</c>).</param>
    /// <param name="cancellationToken">Cancels the relayout.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SetValue(Node control, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        TextEditSession? s = SessionFor(control, create: true);
        if (s is null)
        {
            return InputOutcome.None;
        }

        Node saved = _editElement;
        _editElement = control;
        _editToken = cancellationToken;
        _editOutcome = InputOutcome.None;
        ReadControlAttributes(control, s);
        s.CancelComposition(_editHost!, _lastTime);
        s.Editor.SetValue(s.Multiline ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal));
        if (s.Editor.MaxLength >= 0 && s.Editor.Length > s.Editor.MaxLength)
        {
            s.Editor.SetValue(s.Editor.Text[..s.Editor.MaxLength]);
        }

        s.RebuildDisplay();
        _editHost!.Sync();
        s.UpdateView(_lastTime);
        _editElement = saved;
        return _editOutcome;
    }

    /// <summary>The selection of a text control as <c>selectionStart</c> and <c>selectionEnd</c> report it (UTF-16 offsets into the value).</summary>
    /// <param name="control">A text field or textarea.</param>
    /// <param name="start">The smaller offset.</param>
    /// <param name="end">The larger offset.</param>
    /// <returns>False when the control is not a text control.</returns>
    public bool GetSelection(Node control, out int start, out int end)
    {
        TextEditSession? s = SessionFor(control, create: false);
        start = end = 0;
        if (s is null)
        {
            return IsEditableText(control);
        }

        start = s.Editor.Start;
        end = s.Editor.End;
        return true;
    }

    /// <summary>Sets the selection of a text control as <c>setSelectionRange</c> does: no events, no focus change. Offsets are clamped.</summary>
    /// <param name="control">A text field or textarea.</param>
    /// <param name="start">The anchor offset.</param>
    /// <param name="end">The focus offset (before the anchor selects backward).</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SetSelection(Node control, int start, int end)
    {
        TextEditSession? s = SessionFor(control, create: true);
        if (s is null)
        {
            return InputOutcome.None;
        }

        s.Editor.Select(start, end);
        s.UpdateView(_lastTime);
        return control.Equals(_editElement) ? InputOutcome.EditChanged : InputOutcome.None;
    }

    /// <summary>The text of the focused control's selection; empty for a password field or an empty selection.</summary>
    /// <returns>The selected text.</returns>
    public string GetSelectedText() => Active()?.SelectedText() ?? string.Empty;

    /// <summary>The caret's rectangle in viewport CSS px.</summary>
    /// <param name="rect">The caret; one pixel wide.</param>
    /// <returns>False when no text control has focus or the caret is not laid out.</returns>
    public bool TryGetCaretRect(out Rect rect) => TryEditRect(composition: false, out rect);

    /// <summary>
    /// The rectangle of the IME preedit (or of the caret when no composition is in progress) in viewport CSS px, so the host can place
    /// its candidate window next to it.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>False when no text control has focus or the text is not laid out.</returns>
    public bool TryGetCompositionRect(out Rect rect) => TryEditRect(composition: true, out rect);

    private bool TryEditRect(bool composition, out Rect rect)
    {
        rect = default;
        TextEditSession? s = Active();
        if (s is null || !s.TryGetCompositionRect(_editHost!, out double x, out double y, out double w, out double h))
        {
            return false;
        }

        StyledNode control = _page.Styled.FindBySource(_editElement.Index);
        Fragment frag = _page.Fragments.FragmentOf(_page.Boxes.PrincipalBox(control));
        if (frag.IsNull)
        {
            return false;
        }

        double sx = s.Multiline ? 0 : s.View.ScrollX, sy = 0;
        if (s.Multiline)
        {
            (sx, sy) = _page.Scroll.GetOffset(_page.Fragments, _page.Boxes.PrincipalBox(control));
        }

        Rect v = _hits.ToViewport(frag, new Rect(x - sx, y - sy, composition ? w : 1, h));
        rect = v;
        return true;
    }

    /// <summary>Inserts text typed or produced by the host at the selection of the focused control, as one <c>insertText</c> edit (the engine also does this for printable keys).</summary>
    /// <param name="text">The text.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <param name="cancellationToken">Cancels the relayout.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome InsertText(string text, double time = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return RunEdit(s => s.Insert(_editHost!, text, "insertText", text, TextEditor.EditKind.Typing, time), time, cancellationToken);
    }

    /// <summary>Copies the selection of the focused control to <see cref="Clipboard"/> (fires <c>copy</c>).</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome Copy() => RunEdit(s => { s.Copy(_editHost!, false, _lastTime); return true; }, _lastTime, default);

    /// <summary>Moves the selection of the focused control to <see cref="Clipboard"/> (fires <c>cut</c>, then edits).</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome Cut() => RunEdit(s => { s.Copy(_editHost!, true, _lastTime); return true; }, _lastTime, default);

    /// <summary>Inserts the text of <see cref="Clipboard"/> at the selection of the focused control (fires <c>paste</c>, then edits).</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome Paste() => RunEdit(s => { s.Paste(_editHost!, _lastTime); return true; }, _lastTime, default);

    /// <summary>Undoes the last edit of the focused control.</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome Undo() => RunEdit(s => s.Undo(_editHost!, _lastTime), _lastTime, default);

    /// <summary>Redoes the edit that <see cref="Undo"/> took back.</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome Redo() => RunEdit(s => s.Redo(_editHost!, _lastTime), _lastTime, default);

    /// <summary>Selects all the text of the focused control.</summary>
    /// <returns>What changed on the page.</returns>
    public InputOutcome SelectAll() => RunEdit(s => { s.SetSelection(_editHost!, 0, s.Editor.Length, true, _lastTime); return true; }, _lastTime, default);

    /// <summary>Starts an IME composition at the selection of the focused control (fires <c>compositionstart</c>). <see cref="CompositionUpdate"/> starts one by itself when needed.</summary>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome CompositionStart(double time = 0) => RunEdit(s => { s.CompositionBegin(_editHost!, time); return true; }, time, default);

    /// <summary>
    /// Replaces the preedit text of the composition in progress. It is shown in place, underlined; the committed value does not change until
    /// <see cref="CompositionEnd"/>. Events: <c>beforeinput</c>, <c>compositionupdate</c>, <c>input</c> (all <c>insertCompositionText</c>).
    /// </summary>
    /// <param name="preedit">The preedit text (cut to the control's <c>maxlength</c>).</param>
    /// <param name="caret">The caret inside the preedit, or -1 for its end.</param>
    /// <param name="targetStart">Start of the clause the IME is converting (drawn with a thicker underline), or -1 for none.</param>
    /// <param name="targetEnd">End of that clause.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <param name="cancellationToken">Cancels the relayout.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome CompositionUpdate(string preedit, int caret = -1, int targetStart = -1, int targetEnd = -1, double time = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preedit);
        return RunEdit(s => s.CompositionUpdate(_editHost!, preedit, caret, targetStart, targetEnd, time), time, cancellationToken);
    }

    /// <summary>Ends the composition: <paramref name="committed"/> replaces the preedit in the value, or an empty string cancels it. Fires <c>compositionend</c> last.</summary>
    /// <param name="committed">The final text, or empty to cancel.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <param name="cancellationToken">Cancels the relayout.</param>
    /// <returns>What changed on the page.</returns>
    public InputOutcome CompositionEnd(string committed, double time = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committed);
        return RunEdit(s => s.CompositionFinish(_editHost!, committed, time), time, cancellationToken);
    }

    /// <summary>True while an IME composition is in progress in the focused control.</summary>
    public bool IsComposing => Active()?.Composing ?? false;

    private InputOutcome RunEdit(Func<TextEditSession, bool> action, double time, CancellationToken ct)
    {
        TextEditSession? s = Active();
        if (s is null)
        {
            return InputOutcome.None;
        }

        _lastTime = Math.Max(_lastTime, time);
        _editToken = ct;
        _editOutcome = InputOutcome.None;
        action(s);
        RevealCaret(s);
        return _editOutcome | FlushStates(ct);
    }

    // A textarea scrolls to keep the caret in view (a single-line field does it while painting, see Painter.Editing).
    private void RevealCaret(TextEditSession s)
    {
        if (!s.Multiline)
        {
            return;
        }

        TextGeometry g = EditGeometry();
        StyledNode control = _page.Styled.FindBySource(_editElement.Index);
        Box box = _page.Boxes.PrincipalBox(control);
        if (!g.IsValid || box.IsNull || !g.TryContentRect(out Rect content) || !g.TryCaret(s.View.CaretOffset, out double x, out double y, out double h))
        {
            return;
        }

        (double ox, double oy) = _page.Scroll.GetOffset(_page.Fragments, box);
        double ny = oy;
        if (y < content.Y + oy)
        {
            ny = y - content.Y;
        }
        else if (y + h > content.Y + oy + content.Height)
        {
            ny = y + h - content.Y - content.Height;
        }

        _ = x;
        if (Math.Abs(ny - oy) > 0.001)
        {
            _page.Scroll.SetOffset(box, ox, Math.Max(0, ny));
            _editOutcome |= InputOutcome.Scrolled | InputOutcome.EditChanged;
        }
    }

    private InputOutcome TickCaret(double now)
    {
        TextEditSession? s = Active();
        return s is not null && s.UpdateBlink(now) ? InputOutcome.EditChanged : InputOutcome.None;
    }

    /// <summary>The host time at which the caret next turns on or off, or +infinity when no text control has focus. A host that redraws only on demand can sleep until then, then call <see cref="Advance"/>.</summary>
    /// <param name="now">The host's current time in milliseconds.</param>
    /// <returns>The time of the next blink phase change.</returns>
    public double NextCaretBlinkTime(double now) => Active() is { } s ? s.NextBlinkTime(now) : double.PositiveInfinity;

    // ------------------------------------------------------------------ keys and pointer

    private bool TryEditKey(in KeyInput input, CancellationToken ct, out InputOutcome outcome)
    {
        outcome = InputOutcome.None;
        TextEditSession? s = Active();
        if (s is null || !input.Down)
        {
            return false;
        }

        _editToken = ct;
        _editOutcome = InputOutcome.None;
        ReadControlAttributes(_editElement, s);
        bool handled = s.HandleKey(_editHost!, in input);
        if (!handled && input.Key == "Enter" && !s.Multiline && (input.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            handled = true;
            FireChangeIfNeeded(s, input.Time);
            outcome |= ImplicitSubmit(input.Time, ct);
        }

        if (handled)
        {
            RevealCaret(s);
        }

        outcome |= _editOutcome;
        return handled;
    }

    // HTML §4.10.21.2: Enter in a text field presses the form's default button (its first submit button).
    private InputOutcome ImplicitSubmit(double time, CancellationToken ct)
    {
        Node form = FormOwner(_editElement);
        if (form.IsNull)
        {
            return InputOutcome.None;
        }

        foreach (Node n in form.Descendants)
        {
            if (n.IsElement && IsSubmitter(n) && FormOwner(n).Equals(form))
            {
                return IsDisabledControl(n) ? InputOutcome.None : Activate(n, time);
            }
        }

        return InputOutcome.None;
    }

    private bool EditDocPoint(in PointerInput p, out double x, out double y)
    {
        x = y = 0;
        if (!_hits.HitTest(p.X, p.Y, out HitResult hit) || hit.Fragment.IsNull)
        {
            return false;
        }

        (double ox, double oy) = _page.Fragments.AbsoluteOrigin(hit.Fragment);
        x = ox + hit.LocalX + (_edit!.Multiline ? 0 : _edit.View.ScrollX);
        y = oy + hit.LocalY;
        return true;
    }

    private InputOutcome EditPointerDown(Node target, in PointerInput p, CancellationToken ct)
    {
        TextEditSession? s = Active();
        if (s is null || p.Button != 0 || !IsInclusiveAncestor(_editElement, target) || !EditDocPoint(in p, out double x, out double y))
        {
            return InputOutcome.None;
        }

        _editToken = ct;
        _editOutcome = InputOutcome.None;
        _lastTime = p.Time;
        s.PointerDown(_editHost!, x, y, _clickCount, (p.Modifiers & KeyModifiers.Shift) != 0, p.Time);
        _dragEdit = true;
        _dragDx = x - p.X;
        _dragDy = y - p.Y;
        _dragScroll0 = s.Multiline ? 0 : s.View.ScrollX;
        _editOutcome |= InputOutcome.EditChanged;
        RevealCaret(s);
        return _editOutcome;
    }

    private InputOutcome EditPointerDrag(in PointerInput p)
    {
        TextEditSession? s = Active();
        if (s is null || !_dragEdit)
        {
            _dragEdit = false;
            return InputOutcome.None;
        }

        if (p.Action != PointerAction.Move)
        {
            _dragEdit = false;
            return InputOutcome.None;
        }

        _editOutcome = InputOutcome.None;
        double scroll = s.Multiline ? 0 : s.View.ScrollX - _dragScroll0;
        s.PointerDrag(_editHost!, p.X + _dragDx + scroll, p.Y + _dragDy, p.Time);
        RevealCaret(s);
        return _editOutcome;
    }
}
