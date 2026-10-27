using System.Globalization;
using OneRender.Interaction;

namespace OneRender.Forms;

/// <summary>What a <see cref="TextEditSession"/> needs from the page that owns the control: layout answers, events and a way to put the new text on screen.</summary>
internal abstract class EditHost
{
    /// <summary>Geometry of the control's text as laid out now.</summary>
    public abstract TextGeometry Geometry { get; }

    /// <summary>The host's clipboard, or null.</summary>
    public virtual ITextClipboard? Clipboard => null;

    /// <summary>Fires <c>beforeinput</c>; false when a listener canceled it.</summary>
    public virtual bool BeforeInput(string inputType, string? data, bool composing, double time) => true;

    /// <summary>Fires <c>input</c>.</summary>
    public virtual void Input(string inputType, string? data, bool composing, double time)
    {
    }

    /// <summary>The selection changed through user input; <paramref name="select"/> asks for a <c>select</c> event as well.</summary>
    public virtual void SelectionChanged(bool select, double time)
    {
    }

    /// <summary>Fires a composition event of the given type.</summary>
    public virtual void Composition(string type, string data, double time)
    {
    }

    /// <summary>Fires <c>copy</c>, <c>cut</c> or <c>paste</c>; false when a listener canceled it.</summary>
    public virtual bool ClipboardEvent(string type, double time) => true;

    /// <summary>Writes the session's display text into the tree and lays out what changed.</summary>
    public abstract void Sync();
}

/// <summary>
/// The editing state of one text control: its value and selection, an IME composition in progress, the caret blink and the rules that
/// turn keys and pointer input into edits (UI Events, Input Events Level 2, HTML §4.10.5 and §4.10.11). It is shared by the HTML page
/// and by builder trees; each reaches the tree and the DOM events through an <see cref="EditHost"/>.
/// </summary>
internal sealed class TextEditSession
{
    private const double BlinkHalfPeriod = 500;
    private const char Bullet = '\u2022';

    private char[] _display = new char[64];
    private int _displayLength;
    private char[] _preedit = new char[32];
    private char[] _scratch = new char[64];
    private double _goalX = double.NaN;
    private double _activity;
    private int _wordAnchorStart, _wordAnchorEnd, _granularity;

    public TextEditSession(ControlKind kind)
    {
        Kind = kind;
        Multiline = kind == ControlKind.TextArea;
        Password = kind == ControlKind.Password;
    }

    public ControlKind Kind { get; }

    public TextEditor Editor { get; } = new();

    public TextEditView View { get; } = new();

    public bool Multiline { get; }

    public bool Password { get; }

    /// <summary>The placeholder shown while the value is empty (no line breaks in a single-line field), or empty.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>The value the session started with; the session is untouched while the text still equals it.</summary>
    public string Initial { get; set; } = string.Empty;

    public bool ReadOnly { get; set; }

    public bool Disabled { get; set; }

    /// <summary>Only the characters of a number are accepted (<c>type=number</c>).</summary>
    public bool Numeric { get; set; }

    public bool Editable => !ReadOnly && !Disabled;

    public bool Composing { get; private set; }

    public int ReplaceStart { get; private set; }

    public int ReplaceEnd { get; private set; }

    public int PreeditLength { get; private set; }

    private int _preeditCaret;
    private int _targetStart, _targetEnd;

    /// <summary>The text the control shows: the value with the preedit in place, a password as bullets.</summary>
    public ReadOnlySpan<char> Display => new(_display, 0, _displayLength);

    /// <summary>The value at the moment the control got focus, for deciding whether to fire <c>change</c>.</summary>
    public string? ValueAtFocus { get; set; }

    // ------------------------------------------------------------------ display text

    public void RebuildDisplay()
    {
        ReadOnlySpan<char> v = Editor.Text;
        int cs = Composing ? ReplaceStart : v.Length, ce = Composing ? Math.Min(ReplaceEnd, v.Length) : v.Length;
        int len = Composing ? cs + PreeditLength + (v.Length - ce) : v.Length;

        if (_display.Length < len)
        {
            _display = new char[Math.Max(len, _display.Length * 2)];
        }

        if (Composing)
        {
            v[..cs].CopyTo(_display);
            _preedit.AsSpan(0, PreeditLength).CopyTo(_display.AsSpan(cs));
            v[ce..].CopyTo(_display.AsSpan(cs + PreeditLength));
        }
        else
        {
            v.CopyTo(_display);
        }

        _displayLength = len;
        if (Password)
        {
            _display.AsSpan(0, len).Fill(Bullet);
        }
        else if (!Multiline)
        {
            // A single-line field keeps its text on one line even if a stray break got in.
            Span<char> d = _display.AsSpan(0, len);
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] is '\n' or '\r')
                {
                    d[i] = ' ';
                }
            }
        }
    }

    /// <summary>The display offset of an offset into the value.</summary>
    public int ToDisplay(int committed)
    {
        if (!Composing || committed <= ReplaceStart)
        {
            return committed;
        }

        return committed >= ReplaceEnd ? committed - (ReplaceEnd - ReplaceStart) + PreeditLength : ReplaceStart;
    }

    /// <summary>The offset into the value for a display offset (offsets inside the preedit map to its start).</summary>
    public int ToCommitted(int display)
    {
        if (!Composing || display <= ReplaceStart)
        {
            return display;
        }

        int preEnd = ReplaceStart + PreeditLength;
        return display >= preEnd ? display - PreeditLength + (ReplaceEnd - ReplaceStart) : ReplaceStart;
    }

    public void UpdateView(double time)
    {
        TextEditView w = View;
        int caret = Composing ? ReplaceStart + _preeditCaret : Editor.Focus;
        w.CaretOffset = caret;
        w.SelectionStart = Composing ? caret : ToDisplay(Editor.Start);
        w.SelectionEnd = Composing ? caret : ToDisplay(Editor.End);
        w.CompositionStart = Composing ? ReplaceStart : -1;
        w.CompositionEnd = Composing ? ReplaceStart + PreeditLength : 0;
        w.TargetStart = Composing ? ReplaceStart + _targetStart : 0;
        w.TargetEnd = Composing ? ReplaceStart + _targetEnd : 0;
        w.Multiline = Multiline;
        UpdateBlink(time);
    }

    /// <summary>Marks user activity: the caret shows solid, then blinks.</summary>
    public void Touch(double time) => _activity = time;

    /// <summary>Applies the blink phase for <paramref name="time"/>.</summary>
    /// <returns>True when the caret turned on or off.</returns>
    public bool UpdateBlink(double time)
    {
        double elapsed = Math.Max(0, time - _activity);
        bool visible = Editor.IsCollapsed && Math.Floor(elapsed / BlinkHalfPeriod) % 2 == 0;
        if (Composing)
        {
            visible = Math.Floor(elapsed / BlinkHalfPeriod) % 2 == 0;
        }

        bool changed = visible != View.CaretVisible;
        View.CaretVisible = visible;
        return changed;
    }

    /// <summary>The time at which the caret next turns on or off, so a host can sleep until then.</summary>
    public double NextBlinkTime(double time) => _activity + ((Math.Floor(Math.Max(0, time - _activity) / BlinkHalfPeriod) + 1) * BlinkHalfPeriod);

    // ------------------------------------------------------------------ selection

    public void SetSelection(EditHost h, int anchor, int focus, bool user, double time)
    {
        if (Composing)
        {
            return;
        }

        int a = Editor.Anchor, f = Editor.Focus;
        Editor.Select(anchor, focus);
        _goalX = double.NaN;
        Touch(time);
        UpdateView(time);
        if (a != Editor.Anchor || f != Editor.Focus)
        {
            h.SelectionChanged(user && !Editor.IsCollapsed, time);
        }
    }

    // ------------------------------------------------------------------ editing

    private ReadOnlySpan<char> Filter(ReadOnlySpan<char> text)
    {
        bool needs = false;
        foreach (char c in text)
        {
            if (c == '\r' || (!Multiline && c == '\n') || (Numeric && "0123456789+-.eE".IndexOf(c, StringComparison.Ordinal) < 0))
            {
                needs = true;
                break;
            }
        }

        if (!needs)
        {
            return text;
        }

        if (_scratch.Length < text.Length)
        {
            _scratch = new char[Math.Max(text.Length, _scratch.Length * 2)];
        }

        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    continue;
                }

                c = '\n';
            }

            if (c == '\n' && !Multiline)
            {
                c = ' ';
            }

            if (Numeric && "0123456789+-.eE".IndexOf(c, StringComparison.Ordinal) < 0)
            {
                continue;
            }

            _scratch[n++] = c;
        }

        return _scratch.AsSpan(0, n);
    }

    private void AfterEdit(EditHost h, string inputType, string? data, double time)
    {
        _goalX = double.NaN;
        Touch(time);
        RebuildDisplay();
        UpdateView(time);
        h.Sync();
        h.Input(inputType, data, false, time);
    }

    /// <summary>Inserts text at the selection (<c>insertText</c>, <c>insertFromPaste</c> and the like).</summary>
    public bool Insert(EditHost h, ReadOnlySpan<char> text, string inputType, string? data, TextEditor.EditKind kind, double time)
    {
        if (!Editable || Composing)
        {
            return false;
        }

        text = Filter(text);
        int s = Editor.Start, e = Editor.End;
        if (text.IsEmpty && s == e)
        {
            return false;
        }

        if (Editor.Room(s, e, text) == 0 && !text.IsEmpty && s == e)
        {
            return true;
        }

        if (!h.BeforeInput(inputType, data, false, time))
        {
            return true;
        }

        // A listener may have changed the selection; the plan is made again from where it is now.
        s = Editor.Start;
        e = Editor.End;
        if (!Editor.Replace(s, e, text, kind))
        {
            return true;
        }

        AfterEdit(h, inputType, data, time);
        return true;
    }

    private bool DeleteRange(EditHost h, int s, int e, string inputType, TextEditor.EditKind kind, double time)
    {
        if (!Editable || Composing || s >= e)
        {
            return false;
        }

        if (!h.BeforeInput(inputType, null, false, time))
        {
            return true;
        }

        s = Math.Min(s, Editor.Length);
        e = Math.Min(e, Editor.Length);
        if (Editor.Replace(s, e, default, kind))
        {
            AfterEdit(h, inputType, null, time);
        }

        return true;
    }

    public bool Undo(EditHost h, double time) => History(h, redo: false, time);

    public bool Redo(EditHost h, double time) => History(h, redo: true, time);

    private bool History(EditHost h, bool redo, double time)
    {
        if (!Editable || Composing || !(redo ? Editor.CanRedo : Editor.CanUndo))
        {
            return false;
        }

        string type = redo ? "historyRedo" : "historyUndo";
        if (!h.BeforeInput(type, null, false, time))
        {
            return true;
        }

        if (redo ? Editor.Redo() : Editor.Undo())
        {
            AfterEdit(h, type, null, time);
        }

        return true;
    }

    // ------------------------------------------------------------------ keys

    private static bool IsPrintable(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        if (key.Length == 1)
        {
            return !char.IsControl(key[0]);
        }

        return !char.IsAsciiLetter(key[0]) && StringInfo.GetNextTextElementLength(key) == key.Length;
    }

    /// <summary>
    /// Runs the editing action of a key press. Returns true when the key was an editing key for this control (so the page does not scroll
    /// or activate anything for it), whether or not it changed anything.
    /// </summary>
    public bool HandleKey(EditHost h, in KeyInput k)
    {
        string key = k.Key ?? string.Empty;
        double time = k.Time;
        bool ctrl = (k.Modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool alt = (k.Modifiers & KeyModifiers.Alt) != 0;
        bool shift = (k.Modifiers & KeyModifiers.Shift) != 0;
        TextEditor ed = Editor;
        if (Composing)
        {
            return true; // the IME owns the keys while a composition is open
        }

        if (ctrl && !alt)
        {
            switch (key)
            {
                case "a":
                case "A":
                    SetSelection(h, 0, ed.Length, true, time);
                    return true;
                case "c":
                case "C":
                    Copy(h, cut: false, time);
                    return true;
                case "x":
                case "X":
                    Copy(h, cut: true, time);
                    return true;
                case "v":
                case "V":
                    Paste(h, time);
                    return true;
                case "z":
                case "Z":
                    return shift ? Redo(h, time) | true : Undo(h, time) | true;
                case "y":
                case "Y":
                    return Redo(h, time) | true;
            }
        }

        switch (key)
        {
            case "ArrowLeft":
            case "ArrowRight":
            {
                bool left = key == "ArrowLeft";
                int to;
                if (!shift && !ed.IsCollapsed && !ctrl && !alt)
                {
                    to = left ? ed.Start : ed.End;
                    SetSelection(h, to, to, true, time);
                    return true;
                }

                to = ctrl || alt ? (left ? ed.PreviousWord(ed.Focus) : ed.NextWord(ed.Focus)) : (left ? ed.PreviousCluster(ed.Focus) : ed.NextCluster(ed.Focus));
                SetSelection(h, shift ? ed.Anchor : to, to, true, time);
                return true;
            }

            case "ArrowUp":
            case "ArrowDown":
            {
                bool up = key == "ArrowUp";
                int to;
                if (!Multiline)
                {
                    to = up ? 0 : ed.Length;
                }
                else
                {
                    to = Vertical(h, up ? -1 : 1);
                }

                double keep = _goalX;
                SetSelection(h, shift ? ed.Anchor : to, to, true, time);
                _goalX = keep;
                return true;
            }

            case "Home":
            case "End":
            {
                bool home = key == "Home";
                int to = ctrl || !Multiline
                    ? (home ? 0 : ed.Length)
                    : (home ? VisualLineStart(h, ed.Focus) : VisualLineEnd(h, ed.Focus));
                SetSelection(h, shift ? ed.Anchor : to, to, true, time);
                return true;
            }

            case "Backspace":
                if (!ed.IsCollapsed)
                {
                    return DeleteRange(h, ed.Start, ed.End, "deleteContentBackward", TextEditor.EditKind.Other, time) | true;
                }

                if (ctrl || alt)
                {
                    return DeleteRange(h, ed.PreviousWord(ed.Focus), ed.Focus, "deleteWordBackward", TextEditor.EditKind.Other, time) | true;
                }

                return DeleteRange(h, ed.PreviousCluster(ed.Focus), ed.Focus, "deleteContentBackward", TextEditor.EditKind.DeleteBackward, time) | true;
            case "Delete":
                if (shift && !ctrl && !ed.IsCollapsed)
                {
                    Copy(h, cut: true, time);
                    return true;
                }

                if (!ed.IsCollapsed)
                {
                    return DeleteRange(h, ed.Start, ed.End, "deleteContentForward", TextEditor.EditKind.Other, time) | true;
                }

                if (ctrl || alt)
                {
                    return DeleteRange(h, ed.Focus, ed.NextWord(ed.Focus), "deleteWordForward", TextEditor.EditKind.Other, time) | true;
                }

                return DeleteRange(h, ed.Focus, ed.NextCluster(ed.Focus), "deleteContentForward", TextEditor.EditKind.DeleteForward, time) | true;
            case "Enter":
                if (Multiline)
                {
                    Insert(h, "\n", "insertLineBreak", null, TextEditor.EditKind.Other, time);
                    return true;
                }

                return false;
        }

        if ((!ctrl && !alt || ctrl && alt) && IsPrintable(key))
        {
            Insert(h, key, "insertText", key, TextEditor.EditKind.Typing, time);
            return true;
        }

        return false;
    }

    private int Vertical(EditHost h, int lines)
    {
        TextGeometry g = h.Geometry;
        int focus = ToDisplay(Editor.Focus);
        if (!g.IsValid)
        {
            return Editor.Focus;
        }

        if (double.IsNaN(_goalX) && g.TryCaret(focus, out double x, out _, out _))
        {
            _goalX = x;
        }

        int target = g.MoveVertical(focus, lines, double.IsNaN(_goalX) ? 0 : _goalX);
        if (target == focus)
        {
            // On the first or last line the caret goes to the start or the end of the text.
            return lines < 0 ? 0 : Editor.Length;
        }

        return ToCommitted(Math.Min(target, _displayLength));
    }

    private int VisualLineStart(EditHost h, int offset)
    {
        TextGeometry g = h.Geometry;
        return g.IsValid ? ToCommitted(g.LineStart(ToDisplay(offset))) : Editor.LineStart(offset);
    }

    private int VisualLineEnd(EditHost h, int offset)
    {
        TextGeometry g = h.Geometry;
        return g.IsValid ? ToCommitted(g.LineEnd(ToDisplay(offset))) : Editor.LineEnd(offset);
    }

    // ------------------------------------------------------------------ clipboard

    public string SelectedText() => Editor.IsCollapsed || Password ? string.Empty : Editor.Text[Editor.Start..Editor.End].ToString();

    public void Copy(EditHost h, bool cut, double time)
    {
        if (!h.ClipboardEvent(cut ? "cut" : "copy", time) || Editor.IsCollapsed || Password)
        {
            return;
        }

        ITextClipboard? clip = h.Clipboard;
        clip?.SetText(SelectedText());
        if (cut)
        {
            DeleteRange(h, Editor.Start, Editor.End, "deleteByCut", TextEditor.EditKind.Other, time);
        }
    }

    public void Paste(EditHost h, double time)
    {
        if (!Editable || !h.ClipboardEvent("paste", time))
        {
            return;
        }

        string? text = h.Clipboard?.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            Insert(h, text, "insertFromPaste", text, TextEditor.EditKind.Other, time);
        }
    }

    // ------------------------------------------------------------------ pointer

    /// <summary>A primary-button press at a point in document space. <paramref name="clicks"/> is the click count.</summary>
    public void PointerDown(EditHost h, double x, double y, int clicks, bool shift, double time)
    {
        if (Composing)
        {
            CompositionFinish(h, _preedit.AsSpan(0, PreeditLength).ToString(), time);
        }

        TextGeometry g = h.Geometry;
        int off = g.IsValid ? Math.Min(g.OffsetAt(x, y), _displayLength) : 0;
        off = Math.Clamp(off, 0, Editor.Length);
        _granularity = Math.Min(clicks, 3);
        TextEditor ed = Editor;
        if (_granularity == 2)
        {
            ed.WordAt(off, out _wordAnchorStart, out _wordAnchorEnd);
            SetSelection(h, _wordAnchorStart, _wordAnchorEnd, true, time);
        }
        else if (_granularity >= 3)
        {
            _wordAnchorStart = Multiline ? ed.LineStart(off) : 0;
            _wordAnchorEnd = Multiline ? Math.Min(ed.Length, ed.LineEnd(off) + (ed.LineEnd(off) < ed.Length ? 1 : 0)) : ed.Length;
            SetSelection(h, _wordAnchorStart, _wordAnchorEnd, true, time);
        }
        else
        {
            SetSelection(h, shift ? ed.Anchor : off, off, true, time);
        }
    }

    /// <summary>The pointer moved with the primary button held after <see cref="PointerDown"/>.</summary>
    public void PointerDrag(EditHost h, double x, double y, double time)
    {
        TextGeometry g = h.Geometry;
        if (!g.IsValid)
        {
            return;
        }

        TextEditor ed = Editor;
        int off = Math.Clamp(Math.Min(g.OffsetAt(x, y), _displayLength), 0, ed.Length);
        if (_granularity <= 1)
        {
            SetSelection(h, ed.Anchor, off, true, time);
            return;
        }

        int s, e;
        if (_granularity == 2)
        {
            ed.WordAt(off, out s, out e);
        }
        else
        {
            s = Multiline ? ed.LineStart(off) : 0;
            e = Multiline ? Math.Min(ed.Length, ed.LineEnd(off) + (ed.LineEnd(off) < ed.Length ? 1 : 0)) : ed.Length;
        }

        // The selection always covers the unit pressed on and the unit under the pointer.
        if (s < _wordAnchorStart)
        {
            SetSelection(h, _wordAnchorEnd, s, true, time);
        }
        else
        {
            SetSelection(h, _wordAnchorStart, Math.Max(e, _wordAnchorEnd), true, time);
        }
    }

    // ------------------------------------------------------------------ IME composition (UI Events §5.7, Input Events 2 §5)

    public void CompositionBegin(EditHost h, double time)
    {
        if (Composing || !Editable)
        {
            return;
        }

        Composing = true;
        ReplaceStart = Editor.Start;
        ReplaceEnd = Editor.End;
        PreeditLength = 0;
        _preeditCaret = 0;
        _targetStart = _targetEnd = 0;
        Touch(time);
        h.Composition("compositionstart", SelectedText(), time);
        RebuildDisplay();
        UpdateView(time);
    }

    /// <summary>Replaces the preedit. <paramref name="caret"/> is the caret inside it (-1 for the end); the target clause is [targetStart, targetEnd) (both -1 for none).</summary>
    public bool CompositionUpdate(EditHost h, string preedit, int caret, int targetStart, int targetEnd, double time)
    {
        if (!Editable)
        {
            return false;
        }

        if (!Composing)
        {
            CompositionBegin(h, time);
        }

        int room = Editor.Room(ReplaceStart, ReplaceEnd, preedit);
        string data = room == preedit.Length ? preedit : preedit[..room];
        h.BeforeInput("insertCompositionText", data, true, time);
        h.Composition("compositionupdate", data, time);
        if (_preedit.Length < data.Length)
        {
            _preedit = new char[Math.Max(data.Length, _preedit.Length * 2)];
        }

        data.CopyTo(_preedit);
        PreeditLength = data.Length;
        _preeditCaret = caret < 0 ? data.Length : Math.Min(caret, data.Length);
        _targetStart = targetStart < 0 ? 0 : Math.Min(targetStart, data.Length);
        _targetEnd = targetEnd < 0 ? 0 : Math.Clamp(targetEnd, _targetStart, data.Length);
        Touch(time);
        RebuildDisplay();
        UpdateView(time);
        h.Sync();
        h.Input("insertCompositionText", data, true, time);
        return true;
    }

    /// <summary>Cancels a composition in progress, if any.</summary>
    public void CancelComposition(EditHost h, double time)
    {
        if (Composing)
        {
            CompositionFinish(h, string.Empty, time);
        }
    }

    /// <summary>Ends the composition with <paramref name="committed"/> (empty cancels it).</summary>
    public bool CompositionFinish(EditHost h, string committed, double time)
    {
        if (!Composing)
        {
            return false;
        }

        committed = Filter(committed).ToString();
        if (committed.Length == 0)
        {
            h.BeforeInput("deleteCompositionText", null, true, time);
            h.Composition("compositionupdate", string.Empty, time);
            Composing = false;
            PreeditLength = 0;
            Touch(time);
            RebuildDisplay();
            UpdateView(time);
            h.Sync();
            h.Input("deleteCompositionText", null, true, time);
            h.Composition("compositionend", string.Empty, time);
            return true;
        }

        h.BeforeInput("insertCompositionText", committed, true, time);
        h.Composition("compositionupdate", committed, time);
        int s = ReplaceStart, e = Math.Min(ReplaceEnd, Editor.Length);
        Composing = false;
        PreeditLength = 0;
        Editor.Replace(s, e, committed, TextEditor.EditKind.Typing);
        Touch(time);
        RebuildDisplay();
        UpdateView(time);
        h.Sync();
        h.Input("insertCompositionText", committed, true, time);
        h.Composition("compositionend", committed, time);
        return true;
    }

    /// <summary>The preedit's rectangle (or the caret when it is empty) in document space.</summary>
    public bool TryGetCompositionRect(EditHost h, out double x, out double y, out double width, out double height)
    {
        x = y = width = height = 0;
        TextGeometry g = h.Geometry;
        int s = Composing ? ReplaceStart : ToDisplay(Editor.Focus);
        int e = Composing ? ReplaceStart + PreeditLength : s;
        if (!g.IsValid || !g.TryCaret(s, out x, out y, out height))
        {
            return false;
        }

        if (e > s && g.TryCaret(e, out double x2, out double y2, out _) && y2 == y)
        {
            width = Math.Max(0, x2 - x);
        }

        return true;
    }
}
