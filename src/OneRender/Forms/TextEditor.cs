using System.Globalization;

namespace OneRender.Forms;

/// <summary>
/// The text model behind an editable control: the value as a character buffer, a selection, word and line boundaries, and an undo
/// history. It knows nothing about layout or events, and once its buffers are warm an edit allocates nothing.
/// </summary>
/// <remarks>
/// Offsets are UTF-16 code unit offsets into the value, as the HTML <c>selectionStart</c> and <c>selectionEnd</c> attributes use
/// (HTML §4.10.18.6). Caret movement steps over extended grapheme clusters (UAX #29), so a surrogate pair, a base letter with its
/// combining marks and an emoji sequence each move as one. Line breaks in a value are <c>\n</c> (HTML §4.10.5.4 value sanitization
/// and the API value of a textarea).
/// </remarks>
internal sealed class TextEditor
{
    /// <summary>The longest value, in UTF-16 code units. Longer input is cut, so hostile text cannot grow a value without bound.</summary>
    public const int MaxValueLength = 1 << 20;

    private const int MaxUndoEntries = 2048;
    private const int MaxUndoChars = 1 << 20;
    private const int MaxCoalescedChars = 1024;

    private char[] _buf = new char[64];
    private int _len;

    private UndoEntry[] _undo = new UndoEntry[16];
    private int _undoCount; // entries below this index can be undone
    private int _undoTop; // entries below this index exist (the ones at and above _undoCount can be redone)
    private char[] _arena = new char[256];
    private int _arenaLen;

    private struct UndoEntry
    {
        public int Pos;
        public int RemovedAt; // arena offset of the removed text; the inserted text follows it
        public int RemovedLen;
        public int InsertedLen;
        public int BeforeAnchor;
        public int BeforeFocus;
        public int AfterAnchor;
        public int AfterFocus;
        public EditKind Kind;
    }

    /// <summary>What kind of edit an undo entry records; consecutive edits of the same kind merge into one entry.</summary>
    public enum EditKind : byte
    {
        /// <summary>Not mergeable (a paste, a cut, a replacement).</summary>
        Other,

        /// <summary>Typed characters.</summary>
        Typing,

        /// <summary>Backspace.</summary>
        DeleteBackward,

        /// <summary>The Delete key.</summary>
        DeleteForward,
    }

    /// <summary>The value.</summary>
    public ReadOnlySpan<char> Text => new(_buf, 0, _len);

    /// <summary>The number of code units in the value.</summary>
    public int Length => _len;

    /// <summary>The end of the selection that stays put when it is extended.</summary>
    public int Anchor { get; private set; }

    /// <summary>The end of the selection that moves when it is extended; the caret when the selection is empty.</summary>
    public int Focus { get; private set; }

    /// <summary>The smaller selection offset.</summary>
    public int Start => Math.Min(Anchor, Focus);

    /// <summary>The larger selection offset.</summary>
    public int End => Math.Max(Anchor, Focus);

    /// <summary>True when the selection is a caret.</summary>
    public bool IsCollapsed => Anchor == Focus;

    /// <summary>The <c>maxlength</c> limit in code units, or -1 for none.</summary>
    public int MaxLength { get; set; } = -1;

    /// <summary>True when something can be undone.</summary>
    public bool CanUndo => _undoCount > 0;

    /// <summary>True when something can be redone.</summary>
    public bool CanRedo => _undoCount < _undoTop;

    /// <summary>Replaces the whole value (a script setting <c>value</c>): the history is dropped and the caret goes to the end.</summary>
    /// <param name="text">The new value; cut at <see cref="MaxValueLength"/>.</param>
    public void SetValue(ReadOnlySpan<char> text)
    {
        if (text.Length > MaxValueLength)
        {
            text = text[..MaxValueLength];
        }

        Ensure(text.Length);
        text.CopyTo(_buf);
        _len = text.Length;
        Anchor = Focus = _len;
        ClearHistory();
    }

    /// <summary>Forgets the undo and redo history.</summary>
    public void ClearHistory()
    {
        _undoCount = 0;
        _undoTop = 0;
        _arenaLen = 0;
    }

    /// <summary>Sets the selection, clamped to the value and moved off the middle of a surrogate pair.</summary>
    /// <param name="anchor">The fixed end.</param>
    /// <param name="focus">The moving end.</param>
    public void Select(int anchor, int focus)
    {
        Anchor = Snap(Math.Clamp(anchor, 0, _len));
        Focus = Snap(Math.Clamp(focus, 0, _len));
    }

    private int Snap(int i) => i > 0 && i < _len && char.IsLowSurrogate(_buf[i]) && char.IsHighSurrogate(_buf[i - 1]) ? i - 1 : i;

    private void Ensure(int n)
    {
        if (n > _buf.Length)
        {
            Array.Resize(ref _buf, Math.Max(n, Math.Min(MaxValueLength, _buf.Length * 2)));
        }
    }

    // ------------------------------------------------------------------ editing

    /// <summary>How many code units of <paramref name="incoming"/> the <c>maxlength</c> limit lets replace [start, end).</summary>
    /// <param name="start">Start of the replaced range.</param>
    /// <param name="end">End of the replaced range.</param>
    /// <param name="incoming">The code units to insert.</param>
    /// <returns>The number that fit; a surrogate pair is never cut in half.</returns>
    public int Room(int start, int end, ReadOnlySpan<char> incoming)
    {
        int limit = MaxLength >= 0 ? Math.Min(MaxLength, MaxValueLength) : MaxValueLength;
        int room = limit - (_len - (end - start));
        if (room < 0)
        {
            room = 0;
        }

        int n = Math.Min(room, incoming.Length);
        if (n > 0 && n < incoming.Length && char.IsHighSurrogate(incoming[n - 1]))
        {
            n--;
        }

        return n;
    }

    /// <summary>
    /// Replaces [start, end) with <paramref name="text"/> (cut to fit <see cref="MaxLength"/>), puts the caret after it and records the
    /// change for undo.
    /// </summary>
    /// <param name="start">Start of the replaced range.</param>
    /// <param name="end">End of the replaced range.</param>
    /// <param name="text">The new text.</param>
    /// <param name="kind">How the change merges with the one before.</param>
    /// <returns>True when the value changed.</returns>
    public bool Replace(int start, int end, ReadOnlySpan<char> text, EditKind kind)
    {
        start = Math.Clamp(start, 0, _len);
        end = Math.Clamp(end, start, _len);
        text = text[..Room(start, end, text)];
        if (text.IsEmpty && start == end)
        {
            return false;
        }

        int beforeA = Anchor, beforeF = Focus;
        Record(start, end, text, kind, beforeA, beforeF);
        int removed = end - start;
        int delta = text.Length - removed;
        Ensure(_len + delta);
        if (delta != 0)
        {
            Array.Copy(_buf, end, _buf, end + delta, _len - end);
        }

        text.CopyTo(_buf.AsSpan(start));
        _len += delta;
        Anchor = Focus = start + text.Length;
        if (_undoCount > 0)
        {
            ref UndoEntry e = ref _undo[_undoCount - 1];
            e.AfterAnchor = Anchor;
            e.AfterFocus = Focus;
        }

        return true;
    }

    private void Record(int start, int end, ReadOnlySpan<char> inserted, EditKind kind, int beforeA, int beforeF)
    {
        // A new edit ends the redo branch.
        _undoTop = _undoCount;
        _arenaLen = _undoCount == 0 ? 0 : _undo[_undoCount - 1].RemovedAt + _undo[_undoCount - 1].RemovedLen + _undo[_undoCount - 1].InsertedLen;
        int removedLen = end - start;
        if (_undoCount > 0 && kind != EditKind.Other)
        {
            ref UndoEntry top = ref _undo[_undoCount - 1];
            int size = top.RemovedLen + top.InsertedLen;
            if (top.Kind == kind && size < MaxCoalescedChars)
            {
                if (kind == EditKind.Typing && removedLen == 0 && top.RemovedLen == 0 && start == top.Pos + top.InsertedLen)
                {
                    EnsureArena(inserted.Length);
                    inserted.CopyTo(_arena.AsSpan(_arenaLen));
                    _arenaLen += inserted.Length;
                    top.InsertedLen += inserted.Length;
                    return;
                }

                if (kind == EditKind.DeleteForward && inserted.IsEmpty && top.InsertedLen == 0 && start == top.Pos)
                {
                    EnsureArena(removedLen);
                    _buf.AsSpan(start, removedLen).CopyTo(_arena.AsSpan(_arenaLen));
                    _arenaLen += removedLen;
                    top.RemovedLen += removedLen;
                    return;
                }

                if (kind == EditKind.DeleteBackward && inserted.IsEmpty && top.InsertedLen == 0 && end == top.Pos)
                {
                    EnsureArena(removedLen);
                    int at = top.RemovedAt;
                    Array.Copy(_arena, at, _arena, at + removedLen, top.RemovedLen);
                    _buf.AsSpan(start, removedLen).CopyTo(_arena.AsSpan(at));
                    _arenaLen += removedLen;
                    top.RemovedLen += removedLen;
                    top.Pos = start;
                    return;
                }
            }
        }

        if (_undoCount >= MaxUndoEntries || _arenaLen + removedLen + inserted.Length > MaxUndoChars)
        {
            // Past the limits the history starts over rather than growing.
            _undoCount = 0;
            _undoTop = 0;
            _arenaLen = 0;
        }

        if (_undoCount == _undo.Length)
        {
            Array.Resize(ref _undo, _undo.Length * 2);
        }

        EnsureArena(removedLen + inserted.Length);
        int removedAt = _arenaLen;
        _buf.AsSpan(start, removedLen).CopyTo(_arena.AsSpan(removedAt));
        inserted.CopyTo(_arena.AsSpan(removedAt + removedLen));
        _arenaLen += removedLen + inserted.Length;
        _undo[_undoCount++] = new UndoEntry
        {
            Pos = start,
            RemovedAt = removedAt,
            RemovedLen = removedLen,
            InsertedLen = inserted.Length,
            BeforeAnchor = beforeA,
            BeforeFocus = beforeF,
            Kind = kind,
        };
        _undoTop = _undoCount;
    }

    private void EnsureArena(int extra)
    {
        long need = (long)_arenaLen + extra;
        if (need > _arena.Length)
        {
            Array.Resize(ref _arena, (int)Math.Min(Array.MaxLength, Math.Max(need, (long)_arena.Length * 2)));
        }
    }

    /// <summary>Undoes the last recorded change.</summary>
    /// <returns>True when there was one.</returns>
    public bool Undo()
    {
        if (_undoCount == 0)
        {
            return false;
        }

        ref UndoEntry e = ref _undo[--_undoCount];
        ApplyRaw(e.Pos, e.InsertedLen, _arena.AsSpan(e.RemovedAt, e.RemovedLen));
        Anchor = Math.Min(e.BeforeAnchor, _len);
        Focus = Math.Min(e.BeforeFocus, _len);
        return true;
    }

    /// <summary>Applies the change that <see cref="Undo"/> took back.</summary>
    /// <returns>True when there was one.</returns>
    public bool Redo()
    {
        if (_undoCount >= _undoTop)
        {
            return false;
        }

        ref UndoEntry e = ref _undo[_undoCount++];
        ApplyRaw(e.Pos, e.RemovedLen, _arena.AsSpan(e.RemovedAt + e.RemovedLen, e.InsertedLen));
        Anchor = Math.Min(e.AfterAnchor, _len);
        Focus = Math.Min(e.AfterFocus, _len);
        return true;
    }

    // Replaces `removeLen` code units at `pos` with `with`. The text comes from the arena, never from the buffer.
    private void ApplyRaw(int pos, int removeLen, ReadOnlySpan<char> with)
    {
        pos = Math.Min(pos, _len);
        removeLen = Math.Min(removeLen, _len - pos);
        int delta = with.Length - removeLen;
        Ensure(_len + delta);
        if (delta != 0)
        {
            Array.Copy(_buf, pos + removeLen, _buf, pos + removeLen + delta, _len - pos - removeLen);
        }

        with.CopyTo(_buf.AsSpan(pos));
        _len += delta;
    }

    // ------------------------------------------------------------------ boundaries

    /// <summary>The offset of the grapheme cluster boundary before <paramref name="offset"/>.</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The previous boundary, or 0.</returns>
    public int PreviousCluster(int offset)
    {
        offset = Math.Clamp(offset, 0, _len);
        if (offset == 0)
        {
            return 0;
        }

        // Clusters never span a line break, so scanning forward from a line start (or a bounded distance back) finds the boundary.
        int from = LineStart(offset - 1);
        if (offset - from > 256)
        {
            from = offset - 256;
        }

        int p = from, prev = from;
        while (p < offset)
        {
            prev = p;
            p += StringInfo.GetNextTextElementLength(new ReadOnlySpan<char>(_buf, p, _len - p));
        }

        return prev;
    }

    /// <summary>The offset of the grapheme cluster boundary after <paramref name="offset"/>.</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The next boundary, or the length.</returns>
    public int NextCluster(int offset)
    {
        offset = Math.Clamp(offset, 0, _len);
        if (offset >= _len)
        {
            return _len;
        }

        return Math.Min(_len, offset + Math.Max(1, StringInfo.GetNextTextElementLength(new ReadOnlySpan<char>(_buf, offset, _len - offset))));
    }

    private enum CharClass : byte
    {
        Space,
        Word,
        Punctuation,
        Break,
    }

    private static CharClass ClassOf(char c)
    {
        if (c == '\n')
        {
            return CharClass.Break;
        }

        if (char.IsWhiteSpace(c))
        {
            return CharClass.Space;
        }

        if (char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c))
        {
            return CharClass.Word;
        }

        UnicodeCategory cat = char.GetUnicodeCategory(c);
        return cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ? CharClass.Word : CharClass.Punctuation;
    }

    /// <summary>The start of the word before <paramref name="offset"/>: white space is skipped backward, then a run of the same class.</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The offset.</returns>
    public int PreviousWord(int offset)
    {
        int i = Math.Clamp(offset, 0, _len);
        while (i > 0 && ClassOf(_buf[i - 1]) == CharClass.Space)
        {
            i--;
        }

        if (i > 0 && ClassOf(_buf[i - 1]) == CharClass.Break)
        {
            return i - 1;
        }

        if (i == 0)
        {
            return 0;
        }

        CharClass c = ClassOf(_buf[i - 1]);
        while (i > 0 && ClassOf(_buf[i - 1]) == c)
        {
            i--;
        }

        return i;
    }

    /// <summary>The end of the word at or after <paramref name="offset"/>: white space is skipped forward, then a run of the same class.</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The offset.</returns>
    public int NextWord(int offset)
    {
        int i = Math.Clamp(offset, 0, _len);
        while (i < _len && ClassOf(_buf[i]) == CharClass.Space)
        {
            i++;
        }

        if (i < _len && ClassOf(_buf[i]) == CharClass.Break)
        {
            return i + 1;
        }

        if (i >= _len)
        {
            return _len;
        }

        CharClass c = ClassOf(_buf[i]);
        while (i < _len && ClassOf(_buf[i]) == c)
        {
            i++;
        }

        return i;
    }

    /// <summary>The word, or run of white space or punctuation, that contains the point between characters <paramref name="offset"/> - 1 and <paramref name="offset"/> (a double click).</summary>
    /// <param name="offset">A caret offset.</param>
    /// <param name="start">Where the run starts.</param>
    /// <param name="end">Where it ends.</param>
    public void WordAt(int offset, out int start, out int end)
    {
        offset = Math.Clamp(offset, 0, _len);
        if (_len == 0)
        {
            start = end = 0;
            return;
        }

        // The character under the point; at the end of the text, the one before it.
        int at = offset < _len ? offset : offset - 1;
        CharClass c = ClassOf(_buf[at]);
        if (c == CharClass.Break)
        {
            start = at;
            end = at;
            return;
        }

        start = at;
        end = at + 1;
        while (start > 0 && ClassOf(_buf[start - 1]) == c)
        {
            start--;
        }

        while (end < _len && ClassOf(_buf[end]) == c)
        {
            end++;
        }
    }

    /// <summary>The start of the line (the text after the last <c>\n</c> before <paramref name="offset"/>).</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The offset.</returns>
    public int LineStart(int offset)
    {
        offset = Math.Clamp(offset, 0, _len);
        int i = new ReadOnlySpan<char>(_buf, 0, offset).LastIndexOf('\n');
        return i + 1;
    }

    /// <summary>The end of the line (the offset of the next <c>\n</c>, or the length).</summary>
    /// <param name="offset">A caret offset.</param>
    /// <returns>The offset.</returns>
    public int LineEnd(int offset)
    {
        offset = Math.Clamp(offset, 0, _len);
        int i = new ReadOnlySpan<char>(_buf, offset, _len - offset).IndexOf('\n');
        return i < 0 ? _len : offset + i;
    }
}
