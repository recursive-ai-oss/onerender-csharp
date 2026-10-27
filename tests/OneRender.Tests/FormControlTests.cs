using OneRender.Image;
using OneRender.Forms;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Form controls: Chrome's default sizes, native painting, and the default actions of clicks and keys.</summary>
public class FormControlTests
{
    private static InteractivePage Page(string body, string style = "", int w = 600, int h = 400) => Ix.Page(body, w, h, style);

    private static Layout.Rect Box(InteractivePage p, string id) => p.Layout.GetBoundingClientRect(Ix.Id(p, id));

    // ------------------------------------------------------------------ sizes (Chrome 156, Liberation Sans as Arial; docs/forms.md)

    [Fact]
    public void StretchedControlsFillTheirAreaAndCenterTheirContent()
    {
        InteractivePage p = Page(
            "<div style='display:grid;grid-template-columns:200px 200px;grid-auto-rows:100px'><button id=b style='margin:0'>x</button><input id=r type=range style='margin:0'></div>"
            + "<div style='position:relative;width:300px;height:80px'><input id=t style='position:absolute;left:3px;right:3px;top:3px;bottom:3px;box-sizing:border-box'></div>");
        Layout.Rect b = Box(p, "b"), r = Box(p, "r"), t = Box(p, "t");
        Assert.Equal(100, b.Height, 0.01);
        Assert.Equal(100, r.Height, 0.01);
        Assert.Equal(294, t.Width, 0.01);
        Assert.Equal(74, t.Height, 0.01);
        DisplayList list = Paint(p);
        float y = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.GlyphRun)
            {
                y = list[i].RootBounds.Y;
                break;
            }
        }

        Assert.True(y > 35, y.ToString());
    }

    [Fact]
    public void AccentColorTakesCurrentColorFromTheControl()
    {
        DisplayList list = Paint(Page("<input type=checkbox checked style='color:rgb(255,0,0);accent-color:currentColor'>"));
        Assert.True(HasFill(list, c => c.R > 0.9f && c.G < 0.1f && c.B < 0.1f));
    }

    [Fact]
    public void APercentageHeightThatCannotResolveLeavesAControlItsOwnHeight()
    {
        InteractivePage p = Page("<div><input id=c type=checkbox style='height:100%'></div>");
        Assert.Equal(13, Box(p, "c").Height, 0.01);
    }

    [Fact]
    public void AFieldsetCanBeAColumnSpanner()
    {
        InteractivePage p = Page("<article id=m style='column-count:3;column-rule:6px solid;width:400px'><div>a</div><fieldset id=f style='column-span:all;margin:0;padding:0;border:0'>fieldset</fieldset><div>b</div></article>");
        Assert.True(Box(p, "f").Width > 100);
        Assert.True(Paint(p).Count > 0);
    }

    [Fact]
    public void RelativePositioningShiftsInlineBlocksAndControlsButNotTheLine()
    {
        InteractivePage p = Page("<input id=a><input id=b style='position:relative;left:-10px;top:5px'><div id=d style='display:inline-block;width:20px;position:relative;left:7px'>x</div><input id=c>", "", 900);
        Layout.Rect a = Box(p, "a"), b = Box(p, "b"), d = Box(p, "d"), c = Box(p, "c");
        Assert.Equal(a.X + a.Width - 10, b.X, 0.5);
        Assert.Equal(a.Y + 5, b.Y, 0.5);
        Assert.Equal(a.X + a.Width + b.Width + 7, d.X, 0.5);
        Assert.Equal(a.X + a.Width + b.Width + 20, c.X, 0.5);
    }

    [Fact]
    public void TextFieldsHaveTheDefaultSizeOfTwentyCharacters()
    {
        InteractivePage p = Page("<input id=a><input id=b size=5><input id=c type=password><input id=d type=search style='font-size:20px'>");
        Layout.Rect a = Box(p, "a");
        Assert.Equal(185, a.Width, 0.01);
        Assert.Equal(21, a.Height, 0.01);
        Assert.True(Box(p, "b").Width < 80);
        Assert.Equal(a.Width, Box(p, "c").Width, 0.01);
        Assert.True(Box(p, "d").Height > a.Height);
    }

    [Fact]
    public void CheckboxesRadiosAndRangesHaveWidgetSizes()
    {
        InteractivePage p = Page("<input id=c type=checkbox><input id=r type=radio><input id=g type=range>");
        Assert.Equal((13, 13), (Box(p, "c").Width, Box(p, "c").Height));
        Assert.Equal((13, 13), (Box(p, "r").Width, Box(p, "r").Height));
        Assert.Equal((129, 16), (Box(p, "g").Width, Box(p, "g").Height));
    }

    [Fact]
    public void AppearanceNoneTurnsCheckboxesIntoPlainBoxes()
    {
        InteractivePage p = Page("<input id=c type=checkbox style='appearance:none'>");
        Assert.Equal(0, Box(p, "c").Width);
    }

    [Fact]
    public void ProgressAndMeterAreSizedInEms()
    {
        InteractivePage p = Page("<progress id=p value=0.3></progress><meter id=m value=0.5></meter>");
        Assert.Equal(160, Box(p, "p").Width, 0.05);
        Assert.Equal(80, Box(p, "m").Width, 0.05);
        Assert.Equal(16, Box(p, "p").Height, 0.05);
    }

    [Fact]
    public void ADropDownIsAsWideAsItsWidestOptionPlusItsArrow()
    {
        InteractivePage p = Page("<select id=s><option>a</option><option>A much longer option</option></select><select id=t><option>a</option></select>");
        Assert.True(Box(p, "s").Width > Box(p, "t").Width + 60);
    }

    [Fact]
    public void ButtonsHaveSixPixelsOfPaddingBesideTheLabel()
    {
        InteractivePage p = Page("<button id=b>OK</button><input id=i type=submit>");
        Layout.Rect b = Box(p, "b");
        Assert.True(b.Width > 12 + 4);
        Assert.Equal(21, b.Height, 0.5);
        Assert.True(Box(p, "i").Width > 40);
    }

    [Fact]
    public void FieldsetLegendSitsInTheBorderAndGridFieldsetsLayOutTheRestInTheirGrid()
    {
        InteractivePage p = Page("<fieldset id=f style='display:grid;gap:10px;padding:4px'><legend id=l style='margin-bottom:-4px'>Title</legend><div id=a style='height:20px'></div><div id=b style='height:20px'></div></fieldset>");
        Layout.Rect f = Box(p, "f"), l = Box(p, "l"), a = Box(p, "a"), b = Box(p, "b");
        Assert.Equal(f.Y, l.Y, 0.01);
        // The legend is not a grid item: the first row starts after the legend and the padding, with no gap between them.
        Assert.Equal(l.Y + l.Height - 4 + 4, a.Y, 0.01);
        Assert.Equal(a.Y + 20 + 10, b.Y, 0.01);
    }

    [Fact]
    public void InflexibleFlexItemsContributeTheirMinContentToAMinContentWidth()
    {
        // Chrome probe (forms-probes/avatar/b): `flex: none` does not hold the item at its max-content width in a min-content measurement.
        InteractivePage p = Page("<div id=w style='width:min-content'><div style='display:flex'><div style='flex:none'><span style='display:inline-block;width:160px'></span><span style='display:inline-block;width:36px'></span></div></div></div>");
        Assert.Equal(160, Box(p, "w").Width, 0.01);
    }

    // ------------------------------------------------------------------ painting

    private static DisplayList Paint(InteractivePage p)
    {
        var list = new DisplayList();
        p.Page.Paint(list);
        return list;
    }

    private static bool HasFill(DisplayList list, Func<PaintColor, bool> match)
    {
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if ((it.Kind == ItemKind.FillRoundedRect || it.Kind == ItemKind.FillRect) && it.Paint.Kind == PaintKind.Solid && match(it.Paint.Color))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Blue(PaintColor c) => Math.Abs(c.R) < 0.01 && Math.Abs(c.G - 117 / 255f) < 0.01 && Math.Abs(c.B - 1) < 0.01;

    [Fact]
    public void ACheckedCheckboxIsFilledWithTheAccentColor()
    {
        Assert.True(HasFill(Paint(Page("<input type=checkbox checked>")), Blue));
        Assert.False(HasFill(Paint(Page("<input type=checkbox>")), Blue));
        PaintColor accent = default;
        Assert.True(HasFill(Paint(Page("<input type=checkbox checked style='accent-color:#ff0000'>")), c => { accent = c; return c.R > 0.99f && c.G < 0.01f; }));
        _ = accent;
    }

    [Fact]
    public void RangeProgressAndMeterDrawTheirValue()
    {
        Assert.True(HasFill(Paint(Page("<input type=range value=30>")), Blue));
        Assert.True(HasFill(Paint(Page("<progress value=0.5></progress>")), Blue));
        Assert.True(HasFill(Paint(Page("<meter value=0.5></meter>")), c => Math.Abs(c.G - 124 / 255f) < 0.01f && Math.Abs(c.R - 16 / 255f) < 0.01f));
        Assert.True(HasFill(Paint(Page("<meter min=0 max=10 low=3 high=8 optimum=9 value=2></meter>")), c => Math.Abs(c.R - 216 / 255f) < 0.01f));
    }

    [Fact]
    public void AFieldKeepsItsNativeLookOnlyWhileItsBorderIsTheDefault()
    {
        // Native: one 1 px border drawn by the control; customized: the CSS border (2 px) is drawn as written.
        DisplayList native = Paint(Page("<input>"));
        DisplayList custom = Paint(Page("<input style='border:2px solid red'>"));
        Assert.Equal(1f, MaxBorderWidth(native), 0.01f);
        Assert.Equal(2f, MaxBorderWidth(custom), 0.01f);
    }

    private static float MaxBorderWidth(DisplayList list)
    {
        float max = 0;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.Border)
            {
                max = Math.Max(max, it.BorderTop.Width);
            }
        }

        return max;
    }

    [Fact]
    public void FocusRingIsTwoPixelsOfNearBlack()
    {
        InteractivePage p = Page("<input id=a>");
        p.Focus(Ix.Id(p, "a"));
        DisplayList list = Paint(p);
        bool ring = false;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.Border && Math.Abs(it.BorderTop.Width - 2) < 0.01f && Math.Abs(it.BorderTop.Color.R - 16 / 255f) < 0.01f)
            {
                ring = true;
            }
        }

        Assert.True(ring);
    }

    // ------------------------------------------------------------------ interaction

    private static void Click(InteractivePage p, string id, int dx = 2, int dy = 2)
    {
        Layout.Rect r = Box(p, id);
        double x = r.X + dx, y = r.Y + dy;
        p.HandlePointer(Ix.Move(x, y, 1));
        p.HandlePointer(Ix.Down(x, y, 2));
        p.HandlePointer(Ix.Up(x, y, 3));
    }

    [Fact]
    public void ClickingACheckboxTogglesItAndFiresClickInputAndChangeInOrder()
    {
        InteractivePage p = Page("<input id=c type=checkbox>");
        var log = new List<string>();
        Node c = Ix.Id(p, "c");
        foreach (string t in new[] { "click", "input", "change" })
        {
            p.Events.AddEventListener(EventTarget.Of(c), t, e => log.Add(e.Type + ":" + p.IsChecked(c)));
        }

        Click(p, "c");
        Assert.Equal(["click:True", "input:True", "change:True"], log);
        Assert.True(p.IsChecked(c));
        Click(p, "c");
        Assert.False(p.IsChecked(c));
    }

    [Fact]
    public void ACanceledClickPutsTheCheckboxBack()
    {
        InteractivePage p = Page("<input id=c type=checkbox checked>");
        Node c = Ix.Id(p, "c");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(c), "click", e => e.PreventDefault());
        p.Events.AddEventListener(EventTarget.Of(c), "change", e => log.Add("change"));
        Click(p, "c");
        Assert.True(p.IsChecked(c));
        Assert.Empty(log);
    }

    [Fact]
    public void RadioButtonsOfAGroupExcludeEachOther()
    {
        InteractivePage p = Page("<form><input id=a type=radio name=g checked><input id=b type=radio name=g></form><input id=c type=radio name=g>");
        Node a = Ix.Id(p, "a"), b = Ix.Id(p, "b"), c = Ix.Id(p, "c");
        Click(p, "b");
        Assert.False(p.IsChecked(a));
        Assert.True(p.IsChecked(b));
        Click(p, "c");
        Assert.True(p.IsChecked(b), "the radio outside the form is another group");
        Assert.True(p.IsChecked(c));
        Click(p, "b");
        Assert.True(p.IsChecked(b));
        Click(p, "b");
        Assert.True(p.IsChecked(b), "clicking the checked radio keeps it checked");
    }

    [Fact]
    public void ClickingALabelActivatesItsControl()
    {
        InteractivePage p = Page("<label id=l for=c>Go</label><input id=c type=checkbox><label id=m><input id=d type=checkbox> inner</label>");
        Click(p, "l");
        Assert.True(p.IsChecked(Ix.Id(p, "c")));
        Click(p, "m", 40, 2);
        Assert.True(p.IsChecked(Ix.Id(p, "d")));
    }

    [Fact]
    public void ADisabledControlIgnoresClicks()
    {
        InteractivePage p = Page("<input id=c type=checkbox disabled>");
        Click(p, "c");
        Assert.False(p.IsChecked(Ix.Id(p, "c")));
    }

    [Fact]
    public void SubmitAndResetButtonsFireTheFormEventsWithoutNavigating()
    {
        InteractivePage p = Page("<form id=f><input id=t required><input id=c type=checkbox><button id=s>Go</button><input id=r type=reset></form>");
        Node f = Ix.Id(p, "f");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(f), "submit", e => log.Add("submit:" + ((SubmitEvent)e).Submitter.Node.GetAttribute("id")));
        p.Events.AddEventListener(EventTarget.Of(f), "reset", e => log.Add("reset"));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "t")), "invalid", e => log.Add("invalid"));
        Click(p, "s");
        Assert.Equal(["invalid"], log);
        log.Clear();
        Click(p, "c");
        p.Events.AddEventListener(EventTarget.Of(f), "submit", e => { }, new EventListenerOptions());
        // A novalidate form submits.
        InteractivePage q = Page("<form id=f novalidate><input required><button id=s>Go</button></form>");
        var log2 = new List<string>();
        q.Events.AddEventListener(EventTarget.Of(Ix.Id(q, "f")), "submit", e => log2.Add("submit"));
        Click(q, "s");
        Assert.Equal(["submit"], log2);
        // Reset puts the checkbox back.
        Click(p, "r");
        Assert.Contains("reset", log);
        Assert.False(p.IsChecked(Ix.Id(p, "c")));
    }

    [Fact]
    public void RangeFollowsTheKeyboardAndThePointer()
    {
        InteractivePage p = Page("<input id=g type=range min=0 max=10 value=5 style='width:116px'>");
        Node g = Ix.Id(p, "g");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(g), "input", e => log.Add("input"));
        p.Events.AddEventListener(EventTarget.Of(g), "change", e => log.Add("change"));
        p.Focus(g);
        p.HandleKey(Ix.Key("ArrowRight"));
        Assert.Equal(6, p.GetRangeValue(g));
        Assert.Equal(["input", "change"], log);
        p.HandleKey(Ix.Key("End"));
        Assert.Equal(10, p.GetRangeValue(g));
        p.HandleKey(Ix.Key("Home"));
        Assert.Equal(0, p.GetRangeValue(g));
        log.Clear();
        Layout.Rect r = Box(p, "g");
        p.HandlePointer(Ix.Down(r.X + 8 + 50, r.Y + 8, 1));
        Assert.Equal(5, p.GetRangeValue(g));
        p.HandlePointer(Ix.Move(r.X + 400, r.Y + 8, 2, MouseButtons.Primary));
        Assert.Equal(10, p.GetRangeValue(g));
        p.HandlePointer(Ix.Up(r.X + 400, r.Y + 8, 3));
        Assert.Equal(["input", "input", "change"], log);
    }

    [Fact]
    public void ADropDownOpensChoosesWithTheKeyboardAndClosesOnEscape()
    {
        InteractivePage p = Page("<select id=s><option>One</option><option>Two</option><option>Three</option></select>");
        Node s = Ix.Id(p, "s");
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(s), "change", e => log.Add("change"));
        Assert.Equal(0, p.GetSelectedIndex(s));
        p.Focus(s);
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal(1, p.GetSelectedIndex(s));
        Assert.Equal(["change"], log);
        p.HandleKey(Ix.Key(" "));
        Assert.True(p.IsDropdownOpen(s));
        // The list is a box under the select, above its neighbors.
        Layout.Rect sr = Box(p, "s");
        Node option3 = Ix.Id(p, "s").LastChild;
        Layout.Rect o3 = p.Layout.GetBoundingClientRect(option3);
        Assert.True(o3.Y > sr.Y + sr.Height - 1);
        p.HandleKey(Ix.Key("ArrowDown"));
        p.HandleKey(Ix.Key("Enter"));
        Assert.False(p.IsDropdownOpen(s));
        Assert.Equal(2, p.GetSelectedIndex(s));
        p.HandleKey(Ix.Key(" "));
        Assert.True(p.IsDropdownOpen(s));
        p.HandleKey(Ix.Key("Escape"));
        Assert.False(p.IsDropdownOpen(s));
        Assert.Equal(2, p.GetSelectedIndex(s));
    }

    [Fact]
    public void ClickingAnOptionOfTheOpenListChoosesIt()
    {
        InteractivePage p = Page("<select id=s><option id=a>One</option><option id=b>Two</option></select><div id=below style='height:50px'></div>");
        Node s = Ix.Id(p, "s");
        Click(p, "s");
        Assert.True(p.IsDropdownOpen(s));
        Layout.Rect r = p.Layout.GetBoundingClientRect(Ix.Id(p, "b"));
        p.HandlePointer(Ix.Move(r.X + 3, r.Y + 3, 5));
        p.HandlePointer(Ix.Down(r.X + 3, r.Y + 3, 6));
        p.HandlePointer(Ix.Up(r.X + 3, r.Y + 3, 7));
        Assert.False(p.IsDropdownOpen(s));
        Assert.Equal(1, p.GetSelectedIndex(s));
        // A press elsewhere closes an open list without choosing.
        Click(p, "s");
        Assert.True(p.IsDropdownOpen(s));
        p.HandlePointer(Ix.Down(300, 300, 9));
        Assert.False(p.IsDropdownOpen(s));
        Assert.Equal(1, p.GetSelectedIndex(s));
    }

    [Fact]
    public void ListBoxesSelectTheClickedOption()
    {
        InteractivePage p = Page("<select id=s size=3><option id=a>One</option><option id=b>Two</option><option id=c>Three</option></select>");
        Node s = Ix.Id(p, "s");
        Click(p, "b");
        Assert.Equal(1, p.GetSelectedIndex(s));
        p.Focus(s);
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal(2, p.GetSelectedIndex(s));
    }

    // ------------------------------------------------------------------ builder trees: the same look, no allocation

    [Fact]
    public void TogglingACheckboxAndHoveringAButtonOnABuilderTreeAllocateNothing()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", GameUiScene.Declaration("display:flex;gap:8px;padding:8px;width:300px;height:100px"));
        StyledNode box = tree.AddElement(root, "input", GameUiScene.Declaration("width:13px;height:13px"));
        StyledNode button = tree.AddElement(root, "button", GameUiScene.Declaration("width:60px;height:24px;background-color:#efefef;border:2px outset #767676"));
        tree.SetStateStyle(button, ElementStates.Hover, GameUiScene.Declaration("background-color:#e5e5e5;border-color:#4f4f4f"));
        tree.SetControl(box, ControlKind.Checkbox);
        tree.SetControl(button, ControlKind.Button);
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 320, ViewportHeight = 120 });
        bool on = false, hover = false;
        void Toggle()
        {
            on = !on;
            tree.SetControl(box, ControlKind.Checkbox, on ? ControlStates.Checked : ControlStates.None);
            page.Flush();
        }

        void Hover()
        {
            hover = !hover;
            tree.SetStates(button, hover ? ElementStates.Hover : ElementStates.None);
            page.Flush();
        }

        Toggle();
        Assert.True(HasFill(page.Display.Current, Blue));
        Toggle();
        Assert.False(HasFill(page.Display.Current, Blue));
        Allocation.AssertZero(Toggle, "checkbox toggle through the retained pipeline", warmup: 50, iterations: 100);
        Allocation.AssertZero(Hover, "button hover through the retained pipeline", warmup: 50, iterations: 100);
    }
}
