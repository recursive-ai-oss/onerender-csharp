using OneRender.Forms;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Text editing in text fields and textareas: keys, selection, undo, clipboard, IME composition and their events.</summary>
public class TextEditingTests
{
    private sealed class MemoryClipboard : ITextClipboard
    {
        public string? Text;

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private static InteractivePage Page(string body, string style = "", int w = 600, int h = 400) => Ix.Page(body, w, h, style);

    private static Node Field(InteractivePage p, string id = "t") => Ix.Id(p, id);

    private static InteractivePage Focused(string body = "<input id=t value='hello world'>", string style = "")
    {
        InteractivePage p = Page(body, style);
        p.Focus(Field(p), FocusReason.Script);
        return p;
    }

    private static void Type(InteractivePage p, string text)
    {
        foreach (char c in text)
        {
            p.HandleKey(Ix.Key(c.ToString()));
            p.HandleKey(Ix.Key(c.ToString(), down: false));
        }
    }

    private static void Press(InteractivePage p, string key, KeyModifiers m = KeyModifiers.None) => p.HandleKey(Ix.Key(key, true, m));

    private static string Selected(InteractivePage p)
    {
        p.GetSelection(Field(p), out int s, out int e);
        return p.GetValue(Field(p))[s..e];
    }

    private static string Shown(InteractivePage p, string id = "t")
    {
        StyledNode n = p.Page.Styled.FindBySource(Field(p, id).Index);
        return TextControls.ValueNode(n).Text.ToString();
    }

    // ------------------------------------------------------------------ typing and deleting

    [Fact]
    public void TypingInsertsAtTheCaretAndTheValueFollows()
    {
        InteractivePage p = Focused();
        p.SetSelection(Field(p), 5, 5);
        Type(p, ", dear");
        Assert.Equal("hello, dear world", p.GetValue(Field(p)));
        Assert.Equal("hello, dear world", Shown(p));
        p.GetSelection(Field(p), out int s, out int e);
        Assert.Equal((11, 11), (s, e));
    }

    [Fact]
    public void BackspaceAndDeleteRemoveOneClusterOrTheSelection()
    {
        InteractivePage p = Focused("<input id=t value='ab\U0001F600c'>");
        p.SetSelection(Field(p), 4, 4);
        Press(p, "Backspace");
        Assert.Equal("abc", p.GetValue(Field(p)));
        Press(p, "ArrowLeft");
        Press(p, "Delete");
        Assert.Equal("ac", p.GetValue(Field(p)));
        p.SetSelection(Field(p), 0, 2);
        Press(p, "Backspace");
        Assert.Equal(string.Empty, p.GetValue(Field(p)));
    }

    [Fact]
    public void CtrlArrowsAndCtrlBackspaceWorkByWord()
    {
        InteractivePage p = Focused("<input id=t value='one two  three'>");
        p.SetSelection(Field(p), 0, 0);
        Press(p, "ArrowRight", KeyModifiers.Control);
        p.GetSelection(Field(p), out int s, out _);
        Assert.Equal(3, s);
        Press(p, "ArrowRight", KeyModifiers.Control);
        p.GetSelection(Field(p), out s, out _);
        Assert.Equal(7, s);
        Press(p, "Backspace", KeyModifiers.Control);
        Assert.Equal("one   three", p.GetValue(Field(p)));
        Press(p, "Delete", KeyModifiers.Control);
        Assert.Equal("one ", p.GetValue(Field(p)));
    }

    [Fact]
    public void ShiftExtendsAndArrowsCollapseTheSelection()
    {
        InteractivePage p = Focused();
        p.SetSelection(Field(p), 2, 2);
        Press(p, "ArrowRight", KeyModifiers.Shift);
        Press(p, "ArrowRight", KeyModifiers.Shift);
        Assert.Equal("ll", Selected(p));
        Press(p, "Home", KeyModifiers.Shift);
        Assert.Equal("he", Selected(p));
        Press(p, "End", KeyModifiers.Shift);
        Assert.Equal("llo world", Selected(p));
        Press(p, "ArrowLeft");
        p.GetSelection(Field(p), out int s, out int e);
        Assert.Equal((2, 2), (s, e));
        Press(p, "a", KeyModifiers.Control);
        Assert.Equal("hello world", Selected(p));
    }

    [Fact]
    public void MaxLengthReadOnlyAndDisabledLimitEdits()
    {
        InteractivePage p = Page("<input id=a maxlength=5 value=abc><input id=b readonly value=xy><input id=c disabled value=zz>");
        p.Focus(Field(p, "a"), FocusReason.Script);
        p.SetSelection(Field(p, "a"), 3, 3);
        Type(p, "defgh");
        Assert.Equal("abcde", p.GetValue(Field(p, "a")));
        p.Focus(Field(p, "b"), FocusReason.Script);
        Type(p, "q");
        Press(p, "Backspace");
        Assert.Equal("xy", p.GetValue(Field(p, "b")));
        p.Focus(Field(p, "c"), FocusReason.Script);
        Assert.Equal(InputOutcome.None, p.Focus(Field(p, "c"), FocusReason.Script) & InputOutcome.Relaid);
    }

    [Fact]
    public void PasswordsShowBulletsAndNeverCopy()
    {
        InteractivePage p = Focused("<input id=t type=password value=secret>");
        Assert.Equal(new string('\u2022', 6), Shown(p));
        var clip = new MemoryClipboard();
        p.Clipboard = clip;
        p.SelectAll();
        p.Copy();
        Assert.Null(clip.Text);
        Type(p, "x");
        Assert.Equal("x", p.GetValue(Field(p)));
        Assert.Equal("\u2022", Shown(p));
    }

    [Fact]
    public void NumberFieldsRefuseOtherCharacters()
    {
        InteractivePage p = Focused("<input id=t type=number>");
        Type(p, "1a2.5e");
        Assert.Equal("12.5e", p.GetValue(Field(p)));
    }

    [Fact]
    public void TheValueAttributeKeepsItsLineBreaksOutAndATextareaKeepsThem()
    {
        InteractivePage p = Focused("<textarea id=t rows=3 cols=20>one\ntwo</textarea>");
        p.SetSelection(Field(p), 3, 3);
        Press(p, "Enter");
        Assert.Equal("one\n\ntwo", p.GetValue(Field(p)));
        InteractivePage q = Focused("<input id=t value=ab>");
        Press(q, "Enter");
        Assert.Equal("ab", q.GetValue(Field(q)));
    }

    // ------------------------------------------------------------------ events

    [Fact]
    public void TypingFiresKeydownBeforeinputInputKeyupInOrder()
    {
        InteractivePage p = Focused("<input id=t>");
        var log = new List<string>();
        foreach (string type in new[] { "keydown", "beforeinput", "input", "keyup", "change", "select", "blur" })
        {
            p.Events.AddEventListener(EventTarget.Of(Field(p)), type, e => log.Add(e is InputEvent ie ? e.Type + ":" + ie.InputType + ":" + ie.Data : e.Type));
        }

        Type(p, "a");
        Assert.Equal(["keydown", "beforeinput:insertText:a", "input:insertText:a", "keyup"], log);
        log.Clear();
        Press(p, "Backspace");
        Assert.Equal(["keydown", "beforeinput:deleteContentBackward:", "input:deleteContentBackward:"], log);
        Type(p, "b");
        log.Clear();
        p.Blur();
        Assert.Equal(["change", "blur"], log);
    }

    [Fact]
    public void AnEditMadeNoChangeFiresNoChangeEventAndPreventDefaultOnBeforeinputStopsTheEdit()
    {
        InteractivePage p = Focused("<input id=t value=ab>");
        int changes = 0;
        p.Events.AddEventListener(EventTarget.Of(Field(p)), "change", _ => changes++);
        p.Events.AddEventListener(EventTarget.Of(Field(p)), "beforeinput", e => e.PreventDefault());
        Type(p, "x");
        Assert.Equal("ab", p.GetValue(Field(p)));
        p.Blur();
        Assert.Equal(0, changes);
    }

    [Fact]
    public void EnterFiresChangeAndPressesTheDefaultButton()
    {
        InteractivePage p = Page("<form id=f><input id=t><button id=b>go</button></form>");
        p.Focus(Field(p), FocusReason.Script);
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Field(p)), "change", _ => log.Add("change"));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "b")), "click", _ => log.Add("click"));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "f")), "submit", e => { log.Add("submit"); e.PreventDefault(); });
        Type(p, "x");
        Press(p, "Enter");
        Assert.Equal(["change", "click", "submit"], log);
    }

    [Fact]
    public void SelectingFiresSelectAndSelectionchange()
    {
        InteractivePage p = Focused();
        var log = new List<string>();
        foreach (string type in new[] { "select", "selectionchange" })
        {
            p.Events.AddEventListener(EventTarget.Of(Field(p)), type, e => log.Add(e.Type));
        }

        Press(p, "a", KeyModifiers.Control);
        Assert.Contains("select", log);
        Assert.Contains("selectionchange", log);
    }

    // ------------------------------------------------------------------ undo and clipboard

    [Fact]
    public void UndoAndRedoWalkTypingRunsAndDeletes()
    {
        InteractivePage p = Focused("<input id=t value=ab>");
        p.SetSelection(Field(p), 2, 2);
        Type(p, " cd");
        Press(p, "Backspace", KeyModifiers.Control);
        Assert.Equal("ab ", p.GetValue(Field(p)));
        Press(p, "z", KeyModifiers.Control);
        Assert.Equal("ab cd", p.GetValue(Field(p)));
        Press(p, "z", KeyModifiers.Control);
        Assert.Equal("ab", p.GetValue(Field(p)));
        Press(p, "z", KeyModifiers.Control);
        Assert.Equal("ab", p.GetValue(Field(p)));
        Press(p, "y", KeyModifiers.Control);
        Assert.Equal("ab cd", p.GetValue(Field(p)));
        Press(p, "z", KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal("ab ", p.GetValue(Field(p)));
        Press(p, "z", KeyModifiers.Control);
        Press(p, "z", KeyModifiers.Control);
        Assert.Equal(string.Empty, Selected(p));
    }

    [Fact]
    public void CutCopyAndPasteGoThroughTheHostClipboard()
    {
        InteractivePage p = Focused();
        var clip = new MemoryClipboard();
        p.Clipboard = clip;
        p.SetSelection(Field(p), 0, 5);
        Press(p, "c", KeyModifiers.Control);
        Assert.Equal("hello", clip.Text);
        Press(p, "x", KeyModifiers.Control);
        Assert.Equal(" world", p.GetValue(Field(p)));
        p.SetSelection(Field(p), 6, 6);
        clip.Text = "!\r\n?";
        Press(p, "v", KeyModifiers.Control);
        Assert.Equal(" world! ?", p.GetValue(Field(p)));
    }

    // ------------------------------------------------------------------ IME

    [Fact]
    public void ACompositionShowsThePreeditAndCommitsItsText()
    {
        InteractivePage p = Focused("<input id=t value='ab'>");
        p.SetSelection(Field(p), 1, 1);
        var log = new List<string>();
        foreach (string type in new[] { "compositionstart", "compositionupdate", "compositionend", "beforeinput", "input" })
        {
            p.Events.AddEventListener(EventTarget.Of(Field(p)), type, e => log.Add(e switch
            {
                CompositionEvent c => c.Type + ":" + c.Data,
                InputEvent i => i.Type + ":" + i.InputType + ":" + i.Data + ":" + i.IsComposing,
                _ => e.Type,
            }));
        }

        p.CompositionStart();
        p.CompositionUpdate("n");
        Assert.Equal("anb", Shown(p));
        Assert.Equal("ab", p.GetValue(Field(p)));
        p.CompositionUpdate("ni", targetStart: 0, targetEnd: 2);
        Assert.Equal("anib", Shown(p));
        Assert.True(p.IsComposing);
        Assert.True(p.TryGetCompositionRect(out Layout.Rect r));
        Assert.True(r.Width > 4);
        p.CompositionEnd("\u4f60");
        Assert.Equal("a\u4f60b", p.GetValue(Field(p)));
        Assert.Equal("a\u4f60b", Shown(p));
        Assert.False(p.IsComposing);
        Assert.Equal(
            [
                "compositionstart:",
                "beforeinput:insertCompositionText:n:True", "compositionupdate:n", "input:insertCompositionText:n:True",
                "beforeinput:insertCompositionText:ni:True", "compositionupdate:ni", "input:insertCompositionText:ni:True",
                "beforeinput:insertCompositionText:\u4f60:True", "compositionupdate:\u4f60", "input:insertCompositionText:\u4f60:True", "compositionend:\u4f60",
            ],
            log);
    }

    [Fact]
    public void CancellingACompositionLeavesTheValueAndFiresDeleteCompositionText()
    {
        InteractivePage p = Focused("<input id=t value='ab'>");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Field(p)), "input", e => log.Add(((InputEvent)e).InputType));
        p.CompositionUpdate("xyz");
        Assert.Equal("xyzab", Shown(p));
        p.CompositionEnd(string.Empty);
        Assert.Equal("ab", Shown(p));
        Assert.Equal("ab", p.GetValue(Field(p)));
        Assert.Equal(["insertCompositionText", "deleteCompositionText"], log);
    }

    [Fact]
    public void ThePreeditIsDrawnWithAnUnderlineInTheDisplayList()
    {
        InteractivePage p = Focused("<input id=t>");
        var before = new DisplayList();
        p.Page.Paint(before);
        p.CompositionUpdate("kana");
        var during = new DisplayList();
        p.Page.Paint(during);
        Assert.True(CountThin(during) > CountThin(before));
        bool glyphs = false;
        for (int i = 0; i < during.Count; i++)
        {
            glyphs |= during[i].Kind == ItemKind.GlyphRun;
        }

        Assert.True(glyphs);
    }

    private static int CountThin(DisplayList list)
    {
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.FillRect && list[i].RootBounds.Height <= 1.01f && list[i].RootBounds.Width > 3)
            {
                n++;
            }
        }

        return n;
    }

    // ------------------------------------------------------------------ pointer

    [Fact]
    public void ClickingPlacesTheCaretAndDoubleAndTripleClicksSelectAWordAndAll()
    {
        InteractivePage p = Page("<input id=t value='hello brave world' style='width:300px'>");
        Layout.Rect b = p.Layout.GetBoundingClientRect(Field(p));
        double y = b.Y + (b.Height / 2);
        p.HandlePointer(Ix.Down(b.X + 4, y, 0));
        p.HandlePointer(Ix.Up(b.X + 4, y, 1));
        Assert.Equal(Field(p), p.FocusedElement);
        p.GetSelection(Field(p), out int s, out int e);
        Assert.Equal((0, 0), (s, e));
        // "hello " is about 33 px wide at 13.33 px; a press near x = 45 lands inside "brave".
        p.HandlePointer(Ix.Down(b.X + 45, y, 100));
        p.HandlePointer(Ix.Up(b.X + 45, y, 101));
        p.HandlePointer(Ix.Down(b.X + 45, y, 150));
        Assert.Equal("brave", Selected(p));
        p.HandlePointer(Ix.Up(b.X + 45, y, 151));
        p.HandlePointer(Ix.Down(b.X + 45, y, 200));
        Assert.Equal("hello brave world", Selected(p));
    }

    [Fact]
    public void DraggingExtendsTheSelection()
    {
        InteractivePage p = Page("<input id=t value='hello brave world' style='width:300px'>");
        Layout.Rect b = p.Layout.GetBoundingClientRect(Field(p));
        double y = b.Y + (b.Height / 2);
        p.HandlePointer(Ix.Down(b.X + 3, y, 0));
        p.HandlePointer(Ix.Move(b.X + 60, y, 10, MouseButtons.Primary));
        p.HandlePointer(Ix.Up(b.X + 60, y, 20));
        p.GetSelection(Field(p), out int s, out int e);
        Assert.Equal(0, s);
        Assert.InRange(e, 6, 12);
        p.HandlePointer(Ix.Move(b.X + 200, y, 30, MouseButtons.Primary));
        p.GetSelection(Field(p), out _, out int e2);
        Assert.Equal(e, e2);
    }

    // ------------------------------------------------------------------ textarea

    [Fact]
    public void ArrowsMoveBetweenLinesAndHomeEndStayOnTheVisualLine()
    {
        InteractivePage p = Focused("<textarea id=t cols=8 rows=3>aaa bbb ccc ddd</textarea>");
        p.SetSelection(Field(p), 0, 0);
        Press(p, "ArrowDown");
        p.GetSelection(Field(p), out int s, out _);
        Assert.InRange(s, 3, 9);
        Press(p, "End");
        p.GetSelection(Field(p), out int endOfLine2, out _);
        Assert.True(endOfLine2 >= s);
        Press(p, "Home");
        p.GetSelection(Field(p), out int startOfLine2, out _);
        Assert.True(startOfLine2 > 0 && startOfLine2 <= s);
        Press(p, "ArrowUp");
        p.GetSelection(Field(p), out int up, out _);
        Assert.True(up < startOfLine2);
    }

    [Fact]
    public void ATextareaScrollsToKeepTheCaretInView()
    {
        InteractivePage p = Focused("<textarea id=t rows=2 cols=10></textarea>");
        for (int i = 0; i < 6; i++)
        {
            Press(p, "Enter");
            Type(p, "x");
        }

        Layout.Rect box = p.Layout.GetBoundingClientRect(Field(p));
        Assert.True(p.TryGetCaretRect(out Layout.Rect caret));
        Assert.InRange(caret.Y, box.Y, box.Bottom);
        Assert.True(p.Scroll.GetOffset(p.Page.Fragments, p.Page.Boxes.PrincipalBox(p.Page.Styled.FindBySource(Field(p).Index))).Y > 0);
    }

    [Fact]
    public void AFieldWiderThanItsTextScrollsHorizontallyWithTheCaret()
    {
        InteractivePage p = Focused("<input id=t style='width:60px'>");
        Type(p, "abcdefghijklmnopqrstuvwxyz");
        var list = new DisplayList();
        p.Page.Paint(list);
        Assert.True(p.Page.Form.ActiveView!.ScrollX > 20);
        Assert.True(p.TryGetCaretRect(out Layout.Rect caret));
        Layout.Rect box = p.Layout.GetBoundingClientRect(Field(p));
        Assert.InRange(caret.X, box.X, box.Right);
    }

    // ------------------------------------------------------------------ caret

    [Fact]
    public void TheCaretBlinksOnTheHostClockAndShowsSolidWhileTyping()
    {
        InteractivePage p = Focused("<input id=t value=ab>");
        p.HandleKey(Ix.Key("c") with { Time = 1000 });
        TextEditView v = p.Page.Form.ActiveView!;
        Assert.True(v.CaretVisible);
        Assert.Equal(InputOutcome.None, p.Advance(1400) & InputOutcome.EditChanged);
        Assert.Equal(InputOutcome.EditChanged, p.Advance(1600) & InputOutcome.EditChanged);
        Assert.False(v.CaretVisible);
        Assert.Equal(1500, p.NextCaretBlinkTime(1000) - 0, 0.01);
        p.Advance(2100);
        Assert.True(v.CaretVisible);
    }

    [Fact]
    public void CaretColorAndTheCaretItemAppearInTheDisplayList()
    {
        InteractivePage p = Focused("<input id=t value=ab style='caret-color:#f00'>");
        var list = new DisplayList();
        p.Page.Paint(list);
        bool red = false;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            red |= it.Kind == ItemKind.FillRect && it.Paint.Color.R > 0.9f && it.Paint.Color.G < 0.1f && it.RootBounds.Width <= 1.01f;
        }

        Assert.True(red);
    }

    [Fact]
    public void ASelectionIsHighlightedBehindTheText()
    {
        InteractivePage p = Focused();
        p.SelectAll();
        var list = new DisplayList();
        p.Page.Paint(list);
        int fill = -1, glyph = -1;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.FillRect && it.Paint.Color.B > 0.9f && it.Paint.Color.R < 0.3f && it.RootBounds.Width > 20)
            {
                fill = i;
            }

            if (it.Kind == ItemKind.GlyphRun && fill >= 0 && glyph < 0)
            {
                glyph = i;
            }
        }

        Assert.True(fill >= 0 && glyph > fill);
    }

    // ------------------------------------------------------------------ allocation

    [Fact]
    public void TypingOneCharacterIntoAnHtmlFieldAllocatesNothingOnceWarm()
    {
        InteractivePage p = Focused("<div style='width:300px'><input id=t value='hello'><span>x</span></div>");
        p.SetSelection(Field(p), 5, 5);
        KeyInput add = Ix.Key("a"), back = Ix.Key("Backspace");
        bool flip = false;
        void Step()
        {
            flip = !flip;
            p.HandleKey(flip ? add : back);
        }

        Allocation.AssertZero(Step, "typing a character into an HTML page", warmup: 200, iterations: 100);
    }

    [Fact]
    public void TypingOneCharacterIntoABuilderTreeFieldAllocatesNothingOnceWarm()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", GameUiScene.Declaration("display:block;padding:8px;width:300px;height:100px"));
        StyledNode field = tree.AddElement(root, "input", GameUiScene.Declaration("width:200px;height:24px;overflow:hidden;font:13px Arial"));
        tree.SetControl(field, ControlKind.TextField);
        tree.AddText(field, "hello");
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 320, ViewportHeight = 120 });
        var editor = new LiveTextEditor(page, field);
        editor.Focus(0);
        editor.Select(5, 5);
        KeyInput add = Ix.Key("a"), back = Ix.Key("Backspace");
        bool flip = false;
        void Step()
        {
            flip = !flip;
            editor.HandleKey(flip ? add : back);
        }

        Step();
        Assert.Equal("helloa", editor.Value);
        Step();
        Allocation.AssertZero(Step, "typing a character into a builder tree", warmup: 200, iterations: 100);
        editor.SetValue("");
        editor.HandleKey(Ix.Key("q"));
        Assert.Equal("q", editor.Value);
    }
}
