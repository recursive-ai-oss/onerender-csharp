using System.Text;
using OneRender.Interaction;
using OneRender.Layout;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

public class FocusTests
{
    private static List<string> TabNames(InteractivePage p)
    {
        var names = new List<string>();
        foreach (Node n in p.GetTabOrder())
        {
            names.Add(Ix.IdOf(n));
        }

        return names;
    }

    [Fact]
    public void TabOrderPutsPositiveIndexesFirstThenTreeOrderAndSkipsNegativeAndDisabled()
    {
        InteractivePage p = Ix.Page(
            "<a id=link1 href='#'>a</a>" +
            "<button id=b0>b</button>" +
            "<div id=t2 tabindex=2></div><div id=t1 tabindex=1></div><div id=t1b tabindex=1></div>" +
            "<div id=neg tabindex=-1></div>" +
            "<input id=dis disabled><input id=hidden type=hidden>" +
            "<div id=zero tabindex=0></div><a id=nolink>x</a>" +
            "<div id=gone tabindex=0 style='display:none'></div><div id=inv tabindex=0 style='visibility:hidden'></div>" +
            "<fieldset disabled><input id=infs></fieldset><div inert><button id=inert></button></div>" +
            "<input id=ok>");
        Assert.Equal(["t1", "t1b", "t2", "link1", "b0", "zero", "ok"], TabNames(p));
    }

    [Fact]
    public void TabAndShiftTabWalkTheOrderAndWrap()
    {
        InteractivePage p = Ix.Page("<button id=a></button><button id=b></button><button id=c></button>");
        var order = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            p.HandleKey(Ix.Key("Tab"));
            order.Add(Ix.IdOf(p.FocusedElement));
        }

        Assert.Equal(["a", "b", "c", "a"], order);
        p.HandleKey(Ix.Key("Tab", m: KeyModifiers.Shift));
        Assert.Equal("c", Ix.IdOf(p.FocusedElement));
    }

    [Fact]
    public void WithoutWrappingTabLeavesThePageAtTheEnd()
    {
        InteractivePage p = Ix.Page("<button id=a></button><button id=b></button>", io: new InteractionOptions { WrapTabNavigation = false });
        p.HandleKey(Ix.Key("Tab"));
        p.HandleKey(Ix.Key("Tab"));
        p.HandleKey(Ix.Key("Tab"));
        Assert.True(p.FocusedElement.IsNull);
    }

    [Fact]
    public void ClickingNonFocusableContentStartsTabbingFromThere()
    {
        InteractivePage p = Ix.Page("<button id=a></button><div id=mid style='height:50px'></div><button id=b></button>");
        p.HandlePointer(Ix.Down(5, 30, 1));
        p.HandlePointer(Ix.Up(5, 30, 2));
        Assert.True(p.FocusedElement.IsNull);
        p.HandleKey(Ix.Key("Tab"));
        Assert.Equal("b", Ix.IdOf(p.FocusedElement));
        p.HandlePointer(Ix.Down(5, 30, 10));
        p.HandlePointer(Ix.Up(5, 30, 11));
        p.HandleKey(Ix.Key("Tab", m: KeyModifiers.Shift));
        Assert.Equal("a", Ix.IdOf(p.FocusedElement));
    }

    [Fact]
    public void FocusEventsFireBlurFocusoutFocusFocusinWithRelatedTargets()
    {
        InteractivePage p = Ix.Page("<button id=a></button><button id=b></button>");
        var log = new List<string>();
        foreach (string id in new[] { "a", "b" })
        {
            foreach (string t in new[] { "focus", "blur", "focusin", "focusout" })
            {
                p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, id)), t, e =>
                {
                    var f = (FocusEvent)e;
                    log.Add($"{e.Type}@{id}<{(f.RelatedTarget.IsNull ? "-" : Ix.IdOf(f.RelatedTarget.Node))}");
                });
            }
        }

        p.Focus(Ix.Id(p, "a"));
        Assert.Equal(["focus@a<-", "focusin@a<-"], log);
        log.Clear();
        p.Focus(Ix.Id(p, "b"));
        Assert.Equal(["blur@a<b", "focusout@a<b", "focus@b<a", "focusin@b<a"], log);
        log.Clear();
        p.Blur();
        Assert.Equal(["blur@b<-", "focusout@b<-"], log);
        Assert.True(p.FocusedElement.IsNull);
    }

    [Fact]
    public void FocusinBubblesAndFocusDoesNot()
    {
        InteractivePage p = Ix.Page("<div id=wrap><button id=a></button></div>");
        var log = new List<string>();
        foreach (string t in new[] { "focus", "focusin" })
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "wrap")), t, e => log.Add(e.Type));
        }

        p.Focus(Ix.Id(p, "a"));
        Assert.Equal(["focusin"], log);
    }

    [Fact]
    public void NonFocusableElementsRefuseFocus()
    {
        InteractivePage p = Ix.Page("<div id=plain></div><div id=ti tabindex=-1></div><div id=none tabindex=0 style='display:none'></div>");
        Assert.Equal(InputOutcome.None, p.Focus(Ix.Id(p, "plain")));
        Assert.Equal(InputOutcome.None, p.Focus(Ix.Id(p, "none")));
        Assert.NotEqual(InputOutcome.None, p.Focus(Ix.Id(p, "ti")));
        Assert.Equal("ti", Ix.IdOf(p.FocusedElement));
    }

    private static bool Visible(InteractivePage p)
    {
        foreach (ElementStateChange c in p.LastStateChanges)
        {
            if ((c.After & ElementStates.FocusVisible) != 0)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void FocusVisibleFollowsTheKeyboardPointerHeuristic()
    {
        InteractivePage p = Ix.Page("<button id=b>x</button><input id=t><div id=d tabindex=0 style='height:20px'></div>", extraStyle: "button,input{background-color:rgb(0,0,0)} button:focus-visible{background-color:rgb(255,0,0)} button:focus{outline:1px solid} input:focus-visible{background-color:rgb(0,0,255)}");
        Node b = Ix.Id(p, "b");
        Color(p, "b");

        // A pointer press on a button focuses it without :focus-visible.
        p.HandlePointer(Ix.Down(5, 5, 1));
        Assert.Equal("b", Ix.IdOf(p.FocusedElement));
        Assert.False(Visible(p));
        Assert.Equal(0f, Color(p, "b").R);
        p.HandlePointer(Ix.Up(5, 5, 2));

        // Tabbing away and back shows it.
        p.HandleKey(Ix.Key("Tab", m: KeyModifiers.Shift));
        p.HandleKey(Ix.Key("Tab"));
        Assert.Equal("b", Ix.IdOf(p.FocusedElement));
        Assert.True(Visible(p));
        Assert.Equal(1f, Color(p, "b").R);
        _ = b;

        // A script focusing the next control while the focus ring shows keeps showing it.
        p.Focus(Ix.Id(p, "t"));
        Assert.True(Visible(p));

        // A pointer press on a text field shows the ring (text entry), a press on a div with tabindex does not.
        p.HandlePointer(Ix.Down(5, 5, 100));
        p.HandlePointer(Ix.Up(5, 5, 101));
        Rect dr = p.Layout.GetBoundingClientRect(Ix.Id(p, "d"));
        p.HandlePointer(Ix.Down(5, dr.Y + 5, 200));
        Assert.Equal("d", Ix.IdOf(p.FocusedElement));
        Assert.False(Visible(p));
    }

    private static Style.ColorValue Color(InteractivePage p, string id) => p.Page.Styled.FindBySource(Ix.Id(p, id).Index).Values.BackgroundColor;

    [Fact]
    public void ScriptFocusAfterAPointerPressDoesNotShowTheRing()
    {
        InteractivePage p = Ix.Page("<div id=x style='height:30px'></div><button id=b>x</button><button id=c>y</button>");
        p.HandlePointer(Ix.Down(5, 5, 1));
        p.HandlePointer(Ix.Up(5, 5, 2));
        p.Focus(Ix.Id(p, "b"));
        Assert.False(Visible(p));
        p.HandleKey(Ix.Key("Tab"));
        Assert.True(Visible(p));
    }

    [Fact]
    public void FocusWithinAndFocusStatesCoverTheAncestors()
    {
        InteractivePage p = Ix.Page("<div id=wrap><button id=a></button></div>");
        p.Focus(Ix.Id(p, "a"));
        var states = new Dictionary<string, ElementStates>();
        foreach (ElementStateChange c in p.LastStateChanges)
        {
            states[Ix.IdOf(c.Element)] = c.After;
        }

        Assert.Equal(ElementStates.Focus | ElementStates.FocusWithin | ElementStates.FocusVisible, states["a"]);
        Assert.Equal(ElementStates.FocusWithin, states["wrap"]);
        Assert.Equal(ElementStates.FocusWithin, states["body"]);
    }

    [Fact]
    public void FocusMovesTheFocusedElementIntoViewNearest()
    {
        var sb = new StringBuilder("<div style='height:1000px'></div><button id=far>x</button><div style='height:500px'></div>");
        InteractivePage p = Ix.Page(sb.ToString(), 200, 100);
        p.HandleKey(Ix.Key("Tab"));
        Assert.Equal("far", Ix.IdOf(p.FocusedElement));
        Rect r = p.Layout.GetBoundingClientRect(p.FocusedElement);
        Assert.True(r.Y >= 0 && r.Bottom <= 100, r.ToString());
        // Nearest: the button was below the viewport, so its bottom edge lines up with the viewport's.
        Assert.Equal(100, r.Bottom, 0.5);
    }

    [Fact]
    public void KeyEventsGoToTheFocusedElementAndCancelingStopsTheDefault()
    {
        InteractivePage p = Ix.Page("<button id=a></button><button id=b></button>");
        p.Focus(Ix.Id(p, "a"));
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "a")), "keydown", e => { var k = (KeyboardEvent)e; log.Add(k.Key + "/" + k.Code); e.PreventDefault(); });
        InputOutcome o = p.HandleKey(new KeyInput { Down = true, Key = "Tab", Code = "Tab" });
        Assert.Equal(["Tab/Tab"], log);
        Assert.True((o & InputOutcome.DefaultPrevented) != 0);
        Assert.Equal("a", Ix.IdOf(p.FocusedElement));
    }

    [Fact]
    public void EnterAndSpaceActivateButtonsWithAClick()
    {
        InteractivePage p = Ix.Page("<button id=a></button><a id=l href='#x'>go</a><div id=d tabindex=0></div>");
        int clicks = 0;
        foreach (string id in new[] { "a", "l", "d" })
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, id)), "click", _ => clicks++);
        }

        p.Focus(Ix.Id(p, "a"));
        p.HandleKey(Ix.Key("Enter"));
        p.HandleKey(Ix.Key(" ", down: true));
        Assert.Equal(1, clicks);
        p.HandleKey(Ix.Key(" ", down: false));
        Assert.Equal(2, clicks);
        p.Focus(Ix.Id(p, "l"));
        p.HandleKey(Ix.Key("Enter"));
        Assert.Equal(3, clicks);
        p.Focus(Ix.Id(p, "d"));
        p.HandleKey(Ix.Key("Enter"));
        Assert.Equal(3, clicks);
    }
}

public class WheelAndKeyScrollTests
{
    private const string Nested =
        "<div id=outer style='width:200px;height:100px;overflow:auto'>" +
        "<div id=inner style='width:150px;height:80px;overflow:auto'><div style='height:300px'></div></div>" +
        "<div style='height:400px'></div></div><div style='height:2000px'></div>";

    private static InputOutcome Wheel(InteractivePage p, double x, double y, double dy, double dx = 0, WheelDeltaMode mode = WheelDeltaMode.Pixel, KeyModifiers m = KeyModifiers.None) =>
        p.HandleWheel(new WheelInput { X = x, Y = y, DeltaX = dx, DeltaY = dy, Mode = mode, Modifiers = m, Time = 1 });

    [Fact]
    public void WheelScrollsTheInnermostContainerThenChainsOutward()
    {
        InteractivePage p = Ix.Page(Nested, 300, 150);
        Node inner = Ix.Id(p, "inner"), outer = Ix.Id(p, "outer");
        InputOutcome o = Wheel(p, 10, 10, 50);
        Assert.True((o & InputOutcome.Scrolled) != 0);
        Assert.Equal(50, p.Layout.ScrollTop(inner));
        Assert.Equal(0, p.Layout.ScrollTop(outer));

        // The inner container's range is 300 - 80 = 220; take it to the limit, then further wheel goes to the outer one.
        Wheel(p, 10, 10, 1000);
        Assert.Equal(220, p.Layout.ScrollTop(inner));
        Assert.Equal(0, p.Layout.ScrollTop(outer));
        Wheel(p, 10, 10, 30);
        Assert.Equal(220, p.Layout.ScrollTop(inner));
        Assert.Equal(30, p.Layout.ScrollTop(outer));

        // Scrolling back up: the inner one is at its end, so it takes the upward delta first.
        Wheel(p, 10, 10, -20);
        Assert.Equal(200, p.Layout.ScrollTop(inner));
        Assert.Equal(30, p.Layout.ScrollTop(outer));
    }

    [Fact]
    public void WheelChainsToTheViewportAtTheLimitOfEveryContainer()
    {
        InteractivePage p = Ix.Page(Nested, 300, 150);
        Wheel(p, 10, 10, 5000);
        Wheel(p, 10, 10, 5000);
        Assert.Equal(0, p.Scroll.ViewportY);
        Wheel(p, 10, 10, 120);
        Assert.Equal(120, p.Scroll.ViewportY);
        Assert.Equal(120, p.Layout.ScrollTop(p.Page.Document.DocumentElement));
    }

    [Fact]
    public void WheelOnAnOverflowHiddenBoxDoesNotScrollItButTheNextOneUp()
    {
        InteractivePage p = Ix.Page("<div id=h style='height:50px;overflow:hidden'><div style='height:300px'></div></div><div style='height:1000px'></div>", 200, 100);
        Wheel(p, 5, 5, 30);
        Assert.Equal(0, p.Layout.ScrollTop(Ix.Id(p, "h")));
        Assert.Equal(30, p.Scroll.ViewportY);
    }

    [Fact]
    public void CancelingTheWheelEventPreventsScrolling()
    {
        InteractivePage p = Ix.Page("<div style='height:1000px'></div>", 200, 100);
        p.Events.AddEventListener(EventTarget.Window, "wheel", e => e.PreventDefault());
        InputOutcome o = Wheel(p, 5, 5, 30);
        Assert.True((o & InputOutcome.DefaultPrevented) != 0);
        Assert.Equal(0, p.Scroll.ViewportY);
    }

    [Fact]
    public void LinesPagesAndShiftConvertToPixels()
    {
        InteractivePage p = Ix.Page("<div style='height:3000px;width:3000px'></div>", 200, 100);
        Wheel(p, 5, 5, 2, mode: WheelDeltaMode.Line);
        Assert.Equal(80, p.Scroll.ViewportY);
        Wheel(p, 5, 5, 1, mode: WheelDeltaMode.Page);
        Assert.Equal(80 + 87.5, p.Scroll.ViewportY);
        Wheel(p, 5, 5, 25, m: KeyModifiers.Shift);
        Assert.Equal(25, p.Scroll.ViewportX);
    }

    [Fact]
    public void ScrollEventsFireOnTheContainerWithoutBubblingAndOnTheDocumentWithBubbling()
    {
        InteractivePage p = Ix.Page(Nested, 300, 150);
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "inner")), "scroll", e => log.Add("inner:" + e.Bubbles));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "outer")), "scroll", e => log.Add("outer"));
        p.Events.AddEventListener(p.DocumentTarget, "scroll", e => log.Add("doc:" + e.Bubbles));
        p.Events.AddEventListener(EventTarget.Window, "scroll", e => log.Add("win"));
        Wheel(p, 10, 10, 10);
        Assert.Equal(["inner:False"], log);
        log.Clear();
        p.ScrollViewportTo(0, 40);
        Assert.Equal(["doc:True", "win"], log);
        log.Clear();
        p.ScrollViewportTo(0, 40);
        Assert.Empty(log);
    }

    [Fact]
    public void KeyboardScrollsTheFocusedContainerThenTheViewport()
    {
        InteractivePage p = Ix.Page(Nested.Replace("id=inner", "id=inner tabindex=0", StringComparison.Ordinal), 300, 150);
        p.Focus(Ix.Id(p, "inner"));
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal(40, p.Layout.ScrollTop(Ix.Id(p, "inner")));
        p.HandleKey(Ix.Key("End"));
        Assert.Equal(220, p.Layout.ScrollTop(Ix.Id(p, "inner")));
        p.HandleKey(Ix.Key("PageUp"));
        Assert.Equal(220 - (150 * 0.875), p.Layout.ScrollTop(Ix.Id(p, "inner")), 0.01);
        p.Blur();
        p.HandleKey(Ix.Key(" "));
        Assert.Equal(150 * 0.875, p.Scroll.ViewportY, 0.01);
        p.HandleKey(Ix.Key(" ", m: KeyModifiers.Shift));
        Assert.Equal(0, p.Scroll.ViewportY);
    }

    [Fact]
    public void ArrowKeysAreLeftToTextFields()
    {
        InteractivePage p = Ix.Page("<input id=t><div style='height:2000px'></div>", 200, 100);
        p.Focus(Ix.Id(p, "t"));
        p.HandleKey(Ix.Key("ArrowDown"));
        Assert.Equal(0, p.Scroll.ViewportY);
    }

    [Fact]
    public void HoverFollowsContentScrolledUnderAStillPointer()
    {
        InteractivePage p = Ix.Page("<div id=a style='height:100px'></div><div id=b style='height:100px'></div><div style='height:1000px'></div>", 200, 150);
        p.HandlePointer(Ix.Move(5, 50, 1));
        Assert.Equal("a", Ix.IdOf(p.HoveredElement));
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "b")), "mouseover", _ => log.Add("over-b"));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "a")), "mouseout", _ => log.Add("out-a"));
        Wheel(p, 5, 50, 80);
        Assert.Equal("b", Ix.IdOf(p.HoveredElement));
        Assert.Equal(["out-a", "over-b"], log);
    }
}

public class LayoutQueryTests
{
    private const string Page =
        "<div id=a style='box-sizing:content-box;margin:10px 0 0 20px;width:200px;height:100px;padding:5px;border:3px solid;'>" +
        "<div id=b style='position:relative;left:7px;top:4px;width:50px;height:30px;margin:6px'></div></div>" +
        "<div id=pos style='position:absolute;left:300px;top:50px;width:40px;height:40px'><div id=c style='margin-left:9px;width:10px;height:10px'></div></div>" +
        "<div id=sc style='margin-top:200px;width:100px;height:60px;overflow:auto'><div style='width:300px;height:500px'></div></div>" +
        "<span id=sp>x</span>";

    [Fact]
    public void BoundingRectsOffsetsAndClientSizesOfBoxes()
    {
        InteractivePage p = Ix.Page(Page, 600, 400);
        LayoutQueries q = p.Layout;
        Node a = Ix.Id(p, "a"), b = Ix.Id(p, "b"), c = Ix.Id(p, "c"), pos = Ix.Id(p, "pos");

        Rect ra = q.GetBoundingClientRect(a);
        Assert.Equal((20, 10, 216, 116), (ra.X, ra.Y, ra.Width, ra.Height));
        Assert.Equal(216, q.OffsetWidth(a));
        Assert.Equal(116, q.OffsetHeight(a));
        Assert.Equal(3, q.ClientLeft(a));
        Assert.Equal(3, q.ClientTop(a));
        Assert.Equal(210, q.ClientWidth(a));
        Assert.Equal(110, q.ClientHeight(a));
        // The body is the offset parent; the top margin of #a collapsed through it, so the body itself starts at y = 10.
        Rect rbody = q.GetBoundingClientRect(a.Parent);
        Assert.Equal(10, rbody.Y);
        Assert.Equal(20, q.OffsetLeft(a));
        Assert.Equal(10, q.OffsetTop(a)); // relative to the page origin when the offset parent is the body
        Assert.True(q.OffsetParent(a).Equals(p.Page.Document.DocumentElement.LastChild));

        // b is relatively positioned: moved by 7,4 from its place inside a's content box (20+3+5+6, 10+3+5+6).
        Rect rb = q.GetBoundingClientRect(b);
        Assert.Equal((41, 28, 50, 30), (rb.X, rb.Y, rb.Width, rb.Height));
        Assert.True(q.OffsetParent(b).Equals(a.Parent)); // a is static, so the body is the offset parent
        Assert.Equal(41, q.OffsetLeft(b));

        // c is inside a positioned box: offsets are relative to its padding edge.
        Assert.True(q.OffsetParent(c).Equals(pos));
        Assert.Equal(9, q.OffsetLeft(c));
        Assert.Equal(0, q.OffsetTop(c));
        Assert.Equal(300, q.OffsetLeft(pos));
        Assert.Equal(50, q.OffsetTop(pos));
    }

    [Fact]
    public void RootAndBodyMetrics()
    {
        InteractivePage p = Ix.Page(Page, 600, 400);
        LayoutQueries q = p.Layout;
        Node html = p.Page.Document.DocumentElement;
        Node body = html.LastChild;
        Assert.Equal(600, q.ClientWidth(html));
        Assert.Equal(400, q.ClientHeight(html));
        Assert.True(q.ScrollHeight(html) >= 400);
        Assert.True(q.OffsetParent(body).IsNull);
        Assert.True(q.OffsetParent(html).IsNull);
        Assert.Equal(0, q.OffsetTop(body));
    }

    [Fact]
    public void ScrollMetricsOfAScrollContainer()
    {
        InteractivePage p = Ix.Page(Page, 600, 400);
        LayoutQueries q = p.Layout;
        Node sc = Ix.Id(p, "sc");
        Assert.Equal(100, q.ClientWidth(sc));
        Assert.Equal(60, q.ClientHeight(sc));
        Assert.Equal(300, q.ScrollWidth(sc));
        Assert.Equal(500, q.ScrollHeight(sc));
        Assert.Equal(0, q.ScrollTop(sc));
        p.ScrollElementTo(sc, 50, 120);
        Assert.Equal(50, q.ScrollLeft(sc));
        Assert.Equal(120, q.ScrollTop(sc));
        // Scrolling moves the rectangles the client sees but not the layout offsets.
        Rect inner = q.GetBoundingClientRect(sc);
        Assert.Equal(inner.Y, q.OffsetTop(sc), 1.0); // the body is the offset parent, so the value is from the page origin
    }

    [Fact]
    public void ClientRectsOfScrolledAndTransformedElements()
    {
        InteractivePage p = Ix.Page("<div id=s style='width:100px;height:50px;overflow:auto'><div id=k style='height:20px;margin-top:60px;width:30px;transform:translate(10px,5px)'></div><div style='height:200px'></div></div>", 300, 200);
        Rect k0 = p.Layout.GetBoundingClientRect(Ix.Id(p, "k"));
        Assert.Equal((10, 65, 30, 20), (k0.X, k0.Y, k0.Width, k0.Height));
        p.ScrollElementTo(Ix.Id(p, "s"), 0, 30);
        Rect k1 = p.Layout.GetBoundingClientRect(Ix.Id(p, "k"));
        Assert.Equal((10, 35), (k1.X, k1.Y));
        // The layout size ignores the transform.
        Assert.Equal(30, p.Layout.OffsetWidth(Ix.Id(p, "k")));
        Assert.Equal(60, p.Layout.OffsetTop(Ix.Id(p, "k")));
    }

    [Fact]
    public void InlineBoxesSplitAcrossLinesGiveOneRectPerPiece()
    {
        InteractivePage p = Ix.Page("<div style='width:100px;font:16px Liberation Sans'><span id=s>aaaa bbbb cccc dddd eeee ffff</span></div>", 300, 200);
        var rects = new Rect[16];
        int n = p.Layout.GetClientRects(Ix.Id(p, "s"), rects);
        Assert.True(n >= 3, "expected several lines, got " + n);
        Rect all = p.Layout.GetBoundingClientRect(Ix.Id(p, "s"));
        double top = double.MaxValue, bottom = 0;
        for (int i = 0; i < n; i++)
        {
            top = Math.Min(top, rects[i].Y);
            bottom = Math.Max(bottom, rects[i].Bottom);
        }

        Assert.Equal(top, all.Y, 0.01);
        Assert.Equal(bottom, all.Bottom, 0.01);
    }

    [Fact]
    public void BoxlessElementsAnswerZero()
    {
        InteractivePage p = Ix.Page("<div id=n style='display:none'><span id=in>x</span></div><span id=sp>abc</span>", 300, 200);
        LayoutQueries q = p.Layout;
        Assert.Equal(default, q.GetBoundingClientRect(Ix.Id(p, "n")));
        Assert.Equal(0, q.OffsetWidth(Ix.Id(p, "n")));
        Assert.Equal(0, q.ClientWidth(Ix.Id(p, "n")));
        Assert.Equal(0, q.GetClientRects(Ix.Id(p, "in"), new Rect[4]));
        // An inline box has no client box.
        Assert.Equal(0, q.ClientWidth(Ix.Id(p, "sp")));
        Assert.True(q.OffsetWidth(Ix.Id(p, "sp")) > 0);
    }
}
