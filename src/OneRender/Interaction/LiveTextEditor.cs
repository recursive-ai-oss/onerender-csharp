using OneRender.Forms;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Interaction;

/// <summary>
/// Text editing for a text field or textarea in a styled tree built in code and shown by a <see cref="LivePage"/>: the same caret,
/// selection, undo, clipboard and IME composition behavior as in an HTML page (see <c>docs/forms.md</c>), without DOM events, since a
/// builder tree has no DOM. The host feeds it keys, text, pointer positions and composition steps; the value lives in the field's text
/// child, which the editor keeps up to date and lays out through the page.
/// </summary>
/// <remarks>
/// After every call the page is flushed and painted again, so <see cref="LivePage.Display"/> shows the new value, selection and caret.
/// Once warm, typing a character allocates nothing. Not thread-safe.
/// </remarks>
public sealed class LiveTextEditor
{
    private readonly LivePage _page;
    private readonly StyledNode _field;
    private readonly StyledNode _text;
    private readonly TextEditSession _session;
    private readonly Host _host;
    private bool _focused;
    private bool _repainted;
    private bool _dirty;
    private bool _dragging;

    /// <summary>Creates an editor for <paramref name="field"/>, which should have <see cref="Forms.ControlKind.TextField"/>, <see cref="Forms.ControlKind.Password"/> or <see cref="Forms.ControlKind.TextArea"/> as its control (the kind only picks masking and line breaks). Its first text child is the value; one is added when it has none.</summary>
    /// <param name="page">The page that shows the tree.</param>
    /// <param name="field">The field element. Give it a width (and for a single-line field an <c>overflow</c> of its choice); the editor only places text in it.</param>
    /// <param name="multiline">True for a textarea: Enter inserts a line break and the arrow keys move between lines.</param>
    /// <param name="password">True to show bullets instead of the characters.</param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not a node of the page's tree.</exception>
    public LiveTextEditor(LivePage page, StyledNode field, bool multiline = false, bool password = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (field.IsNull || !ReferenceEquals(field.Tree, page.Styled))
        {
            throw new ArgumentException("The field is not a node of the page's tree.", nameof(field));
        }

        _page = page;
        _field = field;
        _text = TextControls.ValueNode(field);
        if (_text.IsNull)
        {
            _text = page.Styled.AddText(field, default);
        }

        _session = new TextEditSession(multiline ? ControlKind.TextArea : password ? ControlKind.Password : ControlKind.TextField);
        _session.Editor.SetValue(_text.Text);
        _session.Editor.Select(0, 0);
        _session.RebuildDisplay();
        _session.View.ControlNode = field;
        _session.View.Multiline = multiline;
        _host = new Host(this);
    }

    /// <summary>Where copy, cut and paste read and write text; null makes copy do nothing and paste insert nothing.</summary>
    public ITextClipboard? Clipboard { get; set; }

    /// <summary>The longest value in UTF-16 code units, or -1 for no limit (the <c>maxlength</c> attribute). Default -1.</summary>
    public int MaxLength
    {
        get => _session.Editor.MaxLength;
        set => _session.Editor.MaxLength = value;
    }

    /// <summary>True to refuse edits while still allowing selection and copy (<c>readonly</c>). Default false.</summary>
    public bool ReadOnly
    {
        get => _session.ReadOnly;
        set => _session.ReadOnly = value;
    }

    /// <summary>Accept only the characters of a number (<c>type=number</c>). Default false.</summary>
    public bool NumbersOnly
    {
        get => _session.Numeric;
        set => _session.Numeric = value;
    }

    /// <summary>What the painter draws over the field; the editor keeps it up to date and sets it as <see cref="LivePage.Editing"/> while it has focus.</summary>
    public TextEditView View => _session.View;

    /// <summary>The value. Allocates a string.</summary>
    public string Value => _session.Editor.Text.ToString();

    /// <summary>The value without copying; valid until the next edit.</summary>
    public ReadOnlySpan<char> ValueSpan => _session.Editor.Text;

    /// <summary>The start of the selection (<c>selectionStart</c>).</summary>
    public int SelectionStart => _session.Editor.Start;

    /// <summary>The end of the selection (<c>selectionEnd</c>).</summary>
    public int SelectionEnd => _session.Editor.End;

    /// <summary>True while an IME composition is in progress.</summary>
    public bool IsComposing => _session.Composing;

    /// <summary>True while the editor has focus: the caret shows and keys edit.</summary>
    public bool IsFocused => _focused;

    /// <summary>Sets the value as a script would: no undo entry, the caret goes to the end.</summary>
    /// <param name="value">The new value; line breaks are removed in a single-line field.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void SetValue(string value, double time = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        _session.CancelComposition(_host, time);
        _session.Editor.SetValue(value);
        _session.RebuildDisplay();
        _session.UpdateView(time);
        Finish(time);
    }

    /// <summary>Gives the field focus: the caret appears and keys edit. The text is selected when <paramref name="selectAll"/> is true.</summary>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <param name="selectAll">True to select the whole value, as Tab does into a text field.</param>
    public void Focus(double time = 0, bool selectAll = false)
    {
        _focused = true;
        if (selectAll)
        {
            _session.Editor.Select(0, _session.Editor.Length);
        }

        _session.Touch(time);
        _session.UpdateView(time);
        _page.Editing = _session.View;
        Finish(time);
    }

    /// <summary>Takes focus away: a composition in progress is committed and the caret disappears.</summary>
    /// <param name="time">The host's time in milliseconds.</param>
    public void Blur(double time = 0)
    {
        if (!_focused)
        {
            return;
        }

        if (_session.Composing)
        {
            _session.CompositionFinish(_host, _session.Display.Slice(_session.ReplaceStart, _session.PreeditLength).ToString(), time);
        }

        _focused = false;
        _session.View.CaretVisible = false;
        _page.Editing = null;
        Finish(time);
    }

    /// <summary>Handles a key press (release inputs are ignored). Printable keys insert, the arrow, Home, End, Backspace and Delete keys move and delete, Ctrl or Cmd with A, C, X, V, Z and Y select, copy, cut, paste, undo and redo.</summary>
    /// <param name="key">The key input.</param>
    /// <returns>True when the key was an editing key and was used.</returns>
    public bool HandleKey(in KeyInput key)
    {
        if (!_focused || !key.Down)
        {
            return false;
        }

        _dirty = false;
        bool handled = _session.HandleKey(_host, in key);
        if (handled)
        {
            Finish(key.Time);
        }

        return handled;
    }

    /// <summary>Inserts text at the selection as one edit.</summary>
    /// <param name="text">The text.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <returns>False when the field is read-only, not focused, or the text is empty.</returns>
    public bool InsertText(string text, double time = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        bool done = _focused && _session.Insert(_host, text, "insertText", text, TextEditor.EditKind.Typing, time);
        Finish(time);
        return done;
    }

    /// <summary>Starts an IME composition at the selection.</summary>
    /// <param name="time">The host's time in milliseconds.</param>
    public void CompositionStart(double time = 0)
    {
        if (_focused)
        {
            _session.CompositionBegin(_host, time);
            Finish(time);
        }
    }

    /// <summary>Replaces the preedit shown (underlined) in place of the selection; the value is unchanged until <see cref="CompositionEnd"/>.</summary>
    /// <param name="preedit">The preedit text.</param>
    /// <param name="caret">The caret inside the preedit, or -1 for its end.</param>
    /// <param name="targetStart">Start of the clause being converted, or -1.</param>
    /// <param name="targetEnd">End of that clause.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void CompositionUpdate(string preedit, int caret = -1, int targetStart = -1, int targetEnd = -1, double time = 0)
    {
        ArgumentNullException.ThrowIfNull(preedit);
        if (_focused)
        {
            _session.CompositionUpdate(_host, preedit, caret, targetStart, targetEnd, time);
            Finish(time);
        }
    }

    /// <summary>Ends the composition with <paramref name="committed"/>, or cancels it when that is empty.</summary>
    /// <param name="committed">The final text.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void CompositionEnd(string committed, double time = 0)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (_focused)
        {
            _session.CompositionFinish(_host, committed, time);
            Finish(time);
        }
    }

    /// <summary>Sets the selection (clamped to the value).</summary>
    /// <param name="anchor">The fixed end.</param>
    /// <param name="focus">The moving end.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void Select(int anchor, int focus, double time = 0)
    {
        _session.SetSelection(_host, anchor, focus, false, time);
        Finish(time);
    }

    /// <summary>A primary-button press at a point of the page (document space, where the page's layout puts boxes, with the field's own scroll applied by the caller). <paramref name="clicks"/> is 1, 2 for a word and 3 for a line.</summary>
    /// <param name="x">Document x in CSS px.</param>
    /// <param name="y">Document y in CSS px.</param>
    /// <param name="clicks">The click count.</param>
    /// <param name="shift">True to extend the selection.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void PointerDown(double x, double y, int clicks = 1, bool shift = false, double time = 0)
    {
        _session.PointerDown(_host, x + (_session.Multiline ? 0 : _session.View.ScrollX), y, clicks, shift, time);
        _dragging = true;
        Finish(time);
    }

    /// <summary>The pointer moved with the button still down after <see cref="PointerDown"/>: the selection follows it.</summary>
    /// <param name="x">Document x.</param>
    /// <param name="y">Document y.</param>
    /// <param name="time">The host's time in milliseconds.</param>
    public void PointerMove(double x, double y, double time = 0)
    {
        if (_dragging)
        {
            _session.PointerDrag(_host, x + (_session.Multiline ? 0 : _session.View.ScrollX), y, time);
            Finish(time);
        }
    }

    /// <summary>The button was released.</summary>
    public void PointerUp() => _dragging = false;

    /// <summary>Advances the caret blink to <paramref name="time"/>.</summary>
    /// <param name="time">The host's time in milliseconds.</param>
    /// <returns>True when the caret turned on or off and the page was painted again.</returns>
    public bool Advance(double time)
    {
        if (!_focused || !_session.UpdateBlink(time))
        {
            return false;
        }

        _page.Repaint();
        return true;
    }

    /// <summary>The caret rectangle in document CSS px, with the field's horizontal scroll applied.</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top.</param>
    /// <param name="height">Height.</param>
    /// <returns>False when the field is not laid out.</returns>
    public bool TryGetCaretRect(out double x, out double y, out double height)
    {
        y = height = 0;
        bool ok = _session.TryGetCompositionRect(_host, out x, out y, out _, out height);
        x -= _session.Multiline ? 0 : _session.View.ScrollX;
        return ok;
    }

    // The page repaints once per call: the flush already painted when the text changed; a selection or caret change paints on its own.
    private void Finish(double time)
    {
        _ = time;
        if (!_repainted)
        {
            _page.Repaint();
        }
        else if (_dirty)
        {
            _page.Repaint();
        }

        _repainted = false;
        _dirty = false;
    }

    private sealed class Host(LiveTextEditor editor) : EditHost
    {
        public override TextGeometry Geometry =>
            new(editor._page.Fragments, editor._field, editor._text, editor._session.View.LineHeight > 0 ? editor._session.View.LineHeight : 16, editor._session.Multiline);

        public override ITextClipboard? Clipboard => editor.Clipboard;

        public override void SelectionChanged(bool select, double time) => editor._dirty = true;

        public override void Sync()
        {
            TextControls.Sync(editor._page.Styled, editor._field, editor._session.Display, string.Empty);
            editor._page.Flush();
            editor._repainted = true;
        }
    }
}
