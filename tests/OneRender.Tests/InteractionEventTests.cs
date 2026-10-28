using System.Text;
using OneRender.Interaction;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using Xunit;
using ElementStateChange = OneRender.Interaction.ElementStateChange;

namespace OneRender.Tests;

public class DomEventDispatchTests
{
    private const string Tree = "<div id=outer><div id=inner><span id=leaf>x</span></div></div>";

    private static (InteractivePage P, EventDispatcher D, Node Leaf, Node Inner, Node Outer, Node Body, Node Html) Setup()
    {
        InteractivePage p = Ix.Page(Tree);
        Node leaf = Ix.Id(p, "leaf");
        return (p, p.Events, leaf, Ix.Id(p, "inner"), Ix.Id(p, "outer"), p.Page.Document.DocumentElement.LastChild, p.Page.Document.DocumentElement);
    }

    [Fact]
    public void CaptureThenTargetThenBubbleInThatOrder()
    {
        var (p, d, leaf, inner, outer, _, html) = Setup();
        var log = new List<string>();
        foreach ((EventTarget t, string name) in new[] { (EventTarget.Window, "win"), (p.DocumentTarget, "doc"), (EventTarget.Of(html), "html"), (EventTarget.Of(outer), "outer"), (EventTarget.Of(inner), "inner"), (EventTarget.Of(leaf), "leaf") })
        {
            Ix.Record(p, log, t, name + "/cap", capture: true, "ping");
            Ix.Record(p, log, t, name + "/bub", capture: false, "ping");
        }

        Assert.True(d.Dispatch(new DomEvent("ping", bubbles: true), EventTarget.Of(leaf)));
        Assert.Equal(
            [
                "ping@win/cap", "ping@doc/cap", "ping@html/cap", "ping@outer/cap", "ping@inner/cap",
                "ping@leaf/cap*", "ping@leaf/bub*",
                "ping@inner/bub", "ping@outer/bub", "ping@html/bub", "ping@doc/bub", "ping@win/bub",
            ],
            log);
    }

    [Fact]
    public void NonBubblingEventsSkipTheBubbleListenersOfAncestors()
    {
        var (p, d, leaf, inner, _, _, _) = Setup();
        var log = new List<string>();
        Ix.Record(p, log, EventTarget.Of(inner), "inner/cap", true, "ping");
        Ix.Record(p, log, EventTarget.Of(inner), "inner/bub", false, "ping");
        Ix.Record(p, log, EventTarget.Of(leaf), "leaf/bub", false, "ping");
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        Assert.Equal(["ping@inner/cap", "ping@leaf/bub*"], log);
    }

    [Fact]
    public void StopPropagationEndsAfterTheCurrentTargetStopImmediateEndsNow()
    {
        var (p, d, leaf, inner, outer, _, _) = Setup();
        var log = new List<string>();
        d.AddEventListener(EventTarget.Of(inner), "ping", e => { log.Add("inner1"); e.StopPropagation(); });
        d.AddEventListener(EventTarget.Of(inner), "ping", _ => log.Add("inner2"));
        d.AddEventListener(EventTarget.Of(outer), "ping", _ => log.Add("outer"));
        d.Dispatch(new DomEvent("ping", bubbles: true), EventTarget.Of(leaf));
        Assert.Equal(["inner1", "inner2"], log);

        log.Clear();
        InteractivePage q = Ix.Page(Tree);
        Node qi = Ix.Id(q, "inner");
        q.Events.AddEventListener(EventTarget.Of(qi), "ping", e => { log.Add("a"); e.StopImmediatePropagation(); });
        q.Events.AddEventListener(EventTarget.Of(qi), "ping", _ => log.Add("b"));
        q.Events.Dispatch(new DomEvent("ping", bubbles: true), EventTarget.Of(Ix.Id(q, "leaf")));
        Assert.Equal(["a"], log);
        _ = p;
    }

    [Fact]
    public void StopPropagationInTheCapturePhaseKeepsTheTargetFromRunning()
    {
        var (p, d, leaf, _, outer, _, _) = Setup();
        var log = new List<string>();
        d.AddEventListener(EventTarget.Of(outer), "ping", e => { log.Add("outer-cap"); e.StopPropagation(); }, new EventListenerOptions { Capture = true });
        d.AddEventListener(EventTarget.Of(leaf), "ping", _ => log.Add("leaf"));
        d.Dispatch(new DomEvent("ping", bubbles: true), EventTarget.Of(leaf));
        Assert.Equal(["outer-cap"], log);
        _ = p;
    }

    [Fact]
    public void PreventDefaultOnlyCountsForCancelableEventsAndNonPassiveListeners()
    {
        var (_, d, leaf, _, _, _, _) = Setup();
        d.AddEventListener(EventTarget.Of(leaf), "ping", e => e.PreventDefault());
        Assert.True(d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf)));
        var cancelable = new DomEvent("ping", cancelable: true);
        Assert.False(d.Dispatch(cancelable, EventTarget.Of(leaf)));
        Assert.True(cancelable.DefaultPrevented);

        d.AddEventListener(EventTarget.Of(leaf), "pong", e => e.PreventDefault(), new EventListenerOptions { Passive = true });
        Assert.True(d.Dispatch(new DomEvent("pong", cancelable: true), EventTarget.Of(leaf)));
    }

    [Fact]
    public void ListenersAddedDuringDispatchWaitAndRemovedOnesDoNotRun()
    {
        var (_, d, leaf, _, _, _, _) = Setup();
        var log = new List<string>();
        EventListener late = _ => log.Add("late");
        EventListener doomed = _ => log.Add("doomed");
        d.AddEventListener(EventTarget.Of(leaf), "ping", _ =>
        {
            log.Add("first");
            d.AddEventListener(EventTarget.Of(leaf), "ping", late);
            d.RemoveEventListener(EventTarget.Of(leaf), "ping", doomed);
        });
        d.AddEventListener(EventTarget.Of(leaf), "ping", doomed);
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        Assert.Equal(["first"], log);
        log.Clear();
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        Assert.Equal(["first", "late"], log);
    }

    [Fact]
    public void OnceListenersRunOnceAndDuplicatesAreIgnored()
    {
        var (_, d, leaf, _, _, _, _) = Setup();
        int once = 0, dup = 0;
        d.AddEventListener(EventTarget.Of(leaf), "ping", _ => once++, new EventListenerOptions { Once = true });
        EventListener same = _ => dup++;
        d.AddEventListener(EventTarget.Of(leaf), "ping", same);
        d.AddEventListener(EventTarget.Of(leaf), "ping", same);
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        Assert.Equal(1, once);
        Assert.Equal(2, dup);
        // The same callback with the other capture setting is a different listener.
        d.AddEventListener(EventTarget.Of(leaf), "ping", same, new EventListenerOptions { Capture = true });
        d.Dispatch(new DomEvent("ping"), EventTarget.Of(leaf));
        Assert.Equal(4, dup);
    }

    [Fact]
    public void TargetCurrentTargetPhaseAndComposedPathAreSetDuringDispatch()
    {
        var (p, d, leaf, inner, _, _, _) = Setup();
        string seen = string.Empty;
        d.AddEventListener(EventTarget.Of(inner), "ping", e =>
        {
            ReadOnlySpan<EventTarget> path = e.ComposedPath();
            seen = $"{e.Target.Node.GetAttribute("id")}|{e.CurrentTarget.Node.GetAttribute("id")}|{e.Phase}|{path.Length}|{path[0].Node.GetAttribute("id")}|{path[^1].IsWindow}";
        });
        var ev = new DomEvent("ping", bubbles: true);
        d.Dispatch(ev, EventTarget.Of(leaf));
        // leaf, inner, outer, body, html, document, window
        Assert.Equal("leaf|inner|Bubbling|7|leaf|True", seen);
        Assert.Equal(EventPhase.None, ev.Phase);
        Assert.True(ev.CurrentTarget.IsNull);
        Assert.Equal(0, ev.ComposedPath().Length);
        _ = p;
    }

    [Fact]
    public void ReentrantDispatchWorksAndDispatchingTheSameEventTwiceAtOnceThrows()
    {
        var (_, d, leaf, inner, _, _, _) = Setup();
        var log = new List<string>();
        var again = new DomEvent("ping");
        d.AddEventListener(EventTarget.Of(leaf), "ping", e =>
        {
            log.Add("ping");
            if (e == again)
            {
                Assert.Throws<InvalidOperationException>(() => d.Dispatch(again, EventTarget.Of(leaf)));
                d.Dispatch(new DomEvent("pong"), EventTarget.Of(inner));
            }
        });
        d.AddEventListener(EventTarget.Of(inner), "pong", _ => log.Add("pong"));
        d.Dispatch(again, EventTarget.Of(leaf));
        Assert.Equal(["ping", "pong"], log);
    }

    [Fact]
    public void LoadEventsDoNotReachTheWindow()
    {
        var (p, d, leaf, _, _, _, _) = Setup();
        int win = 0, doc = 0;
        d.AddEventListener(EventTarget.Window, "load", _ => win++);
        d.AddEventListener(p.DocumentTarget, "load", _ => doc++);
        d.Dispatch(new DomEvent("load", bubbles: true), EventTarget.Of(leaf));
        Assert.Equal(0, win);
        Assert.Equal(1, doc);
    }

    [Fact]
    public void ForeignDocumentTargetsAreRejected()
    {
        var (_, d, _, _, _, _, _) = Setup();
        InteractivePage other = Ix.Page(Tree);
        Assert.Throws<ArgumentException>(() => d.Dispatch(new DomEvent("ping"), EventTarget.Of(Ix.Id(other, "leaf"))));
        Assert.Throws<ArgumentException>(() => d.Dispatch(new DomEvent("ping"), default));
    }

    [Fact]
    public void VeryDeepTreesGetACappedPathButStillReachDocumentAndWindow()
    {
        var sb = new StringBuilder();
        int depth = 4000;
        for (int i = 0; i < depth; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("<span id=leaf></span>");
        InteractivePage p = Ix.Page(sb.ToString());
        Node leaf = Ix.Id(p, "leaf");
        int win = 0, doc = 0, path = 0;
        p.Events.AddEventListener(EventTarget.Window, "ping", _ => win++);
        p.Events.AddEventListener(p.DocumentTarget, "ping", _ => doc++);
        p.Events.AddEventListener(EventTarget.Of(leaf), "ping", e => path = e.ComposedPath().Length);
        p.Events.Dispatch(new DomEvent("ping", bubbles: true), EventTarget.Of(leaf));
        Assert.Equal(1, win);
        Assert.Equal(1, doc);
        Assert.True(path <= EventDispatcher.MaxPathLength, path.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

public class PointerEventOrderTests
{
    private const string Nest = "<div id=a style='width:100px;height:100px'><div id=b style='width:50px;height:50px'></div></div><div id=c style='width:100px;height:100px'></div>";

    private static List<string> Watch(InteractivePage p, params string[] types)
    {
        var log = new List<string>();
        foreach (string id in new[] { "a", "b", "c" })
        {
            foreach (string t in types)
            {
                p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, id)), t, e =>
                {
                    string rel = e is MouseEvent m ? (m.RelatedTarget.IsNull ? string.Empty : "<" + Ix.IdOf(m.RelatedTarget.Node)) : string.Empty;
                    log.Add(e.Type + "@" + id + rel);
                });
            }
        }

        return log;
    }

    [Fact]
    public void MovingIntoNestedThenSiblingFiresOverOutEnterLeaveInOrder()
    {
        InteractivePage p = Ix.Page(Nest);
        List<string> log = Watch(p, "mouseover", "mouseout", "mouseenter", "mouseleave", "mousemove");
        p.HandlePointer(Ix.Move(200, 200, 1));
        Assert.Empty(log);
        Assert.Equal("html", Ix.IdOf(p.HoveredElement));

        p.HandlePointer(Ix.Move(10, 10, 2));
        // Entering b from the canvas (html): over on b (bubbles to a), then enter a, enter b (outermost first), then the move.
        Assert.Equal(["mouseover@b<html", "mouseover@a<html", "mouseenter@a<html", "mouseenter@b<html", "mousemove@b", "mousemove@a"], log);

        log.Clear();
        p.HandlePointer(Ix.Move(70, 70, 3));
        // From b to a (b's parent): out on b, leave b only, over on a.
        Assert.Equal(["mouseout@b<a", "mouseout@a<a", "mouseleave@b<a", "mouseover@a<b", "mousemove@a"], log);

        log.Clear();
        p.HandlePointer(Ix.Move(70, 150, 4));
        Assert.Equal(["mouseout@a<c", "mouseleave@a<c", "mouseover@c<a", "mouseenter@c<a", "mousemove@c"], log);

        log.Clear();
        p.HandlePointer(Ix.Move(71, 151, 5));
        Assert.Equal(["mousemove@c"], log);
    }

    [Fact]
    public void PointerEventsPrecedeTheirMouseCounterparts()
    {
        InteractivePage p = Ix.Page(Nest);
        var log = new List<string>();
        foreach (string t in new[] { "pointerover", "mouseover", "pointerenter", "mouseenter", "pointermove", "mousemove" })
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), t, e => log.Add(e.Type));
        }

        p.HandlePointer(Ix.Move(10, 150, 1));
        Assert.Equal(["pointerover", "mouseover", "pointerenter", "mouseenter", "pointermove", "mousemove"], log);
    }

    [Fact]
    public void PressReleaseSynthesizesClickOnTheCommonAncestor()
    {
        InteractivePage p = Ix.Page(Nest);
        List<string> log = Watch(p, "pointerdown", "mousedown", "pointerup", "mouseup", "click", "dblclick");
        p.HandlePointer(Ix.Move(10, 10, 1));
        log.Clear();
        p.HandlePointer(Ix.Down(10, 10, 2));
        p.HandlePointer(Ix.Up(10, 10, 3));
        Assert.Equal(
            ["pointerdown@b", "pointerdown@a", "mousedown@b", "mousedown@a", "pointerup@b", "pointerup@a", "mouseup@b", "mouseup@a", "click@b", "click@a"],
            log);

        // Press on b, release on a's own area: click goes to the common ancestor, a.
        log.Clear();
        p.HandlePointer(Ix.Down(10, 10, 1000));
        p.HandlePointer(Ix.Move(80, 80, 1001, MouseButtons.Primary));
        p.HandlePointer(Ix.Up(80, 80, 1002));
        Assert.Contains("click@a", log);
        Assert.DoesNotContain("click@b", log);
    }

    [Fact]
    public void ClickCountAndDoubleClickUseHostTimestamps()
    {
        InteractivePage p = Ix.Page(Nest);
        var counts = new List<int>();
        var types = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), "click", e => counts.Add(((UIEvent)e).Detail));
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), "dblclick", e => types.Add(e.Type));
        p.HandlePointer(Ix.Down(10, 150, 100));
        p.HandlePointer(Ix.Up(10, 150, 120));
        p.HandlePointer(Ix.Down(11, 150, 300));
        p.HandlePointer(Ix.Up(11, 150, 320));
        Assert.Equal([1, 2], counts);
        Assert.Equal(["dblclick"], types);

        // Too slow: the count restarts.
        p.HandlePointer(Ix.Down(11, 150, 5000));
        p.HandlePointer(Ix.Up(11, 150, 5020));
        Assert.Equal([1, 2, 1], counts);
        Assert.Single(types);
    }

    [Fact]
    public void SecondaryAndAuxiliaryButtonsFireTheirOwnEvents()
    {
        InteractivePage p = Ix.Page(Nest);
        var log = new List<string>();
        foreach (string t in new[] { "click", "auxclick", "contextmenu" })
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), t, e => log.Add(e.Type + ((MouseEvent)e).Button.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        p.HandlePointer(Ix.Down(10, 150, 1, button: 1));
        p.HandlePointer(Ix.Up(10, 150, 2, button: 1));
        p.HandlePointer(Ix.Down(10, 150, 3, button: 2));
        p.HandlePointer(Ix.Up(10, 150, 4, button: 2));
        Assert.Equal(["auxclick1", "contextmenu2"], log);
    }

    [Fact]
    public void CancelingPointerDownSuppressesMouseDownUntilRelease()
    {
        InteractivePage p = Ix.Page(Nest);
        var log = new List<string>();
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), "pointerdown", e => { log.Add("pd"); e.PreventDefault(); });
        foreach (string t in new[] { "mousedown", "mouseup", "pointerup", "mousemove" })
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "c")), t, e => log.Add(e.Type));
        }

        p.HandlePointer(Ix.Down(10, 150, 1));
        p.HandlePointer(Ix.Move(12, 150, 2, MouseButtons.Primary));
        p.HandlePointer(Ix.Up(12, 150, 3));
        Assert.Equal(["pd", "pointerup"], log);
        log.Clear();
        p.HandlePointer(Ix.Move(14, 150, 4));
        Assert.Equal(["mousemove"], log);
    }

    [Fact]
    public void LeavingTheViewportFiresOutAndLeaveWithNoRelatedTarget()
    {
        InteractivePage p = Ix.Page(Nest);
        List<string> log = Watch(p, "mouseout", "mouseleave");
        p.HandlePointer(Ix.Move(10, 10, 1));
        log.Clear();
        p.HandlePointer(new PointerInput { Action = PointerAction.Leave, X = -5, Y = -5, Time = 2 });
        Assert.Equal(["mouseout@b", "mouseout@a", "mouseleave@b", "mouseleave@a"], log);
        Assert.True(p.HoveredElement.IsNull);
    }

    [Fact]
    public void PagePositionAddsTheViewportScroll()
    {
        InteractivePage p = Ix.Page("<div id=a style='height:1000px'></div>", 200, 100);
        p.ScrollViewportTo(0, 300);
        double px = 0, py = 0, cy = 0;
        p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, "a")), "mousemove", e =>
        {
            var m = (MouseEvent)e;
            px = m.PageX;
            py = m.PageY;
            cy = m.ClientY;
        });
        p.HandlePointer(Ix.Move(7, 20, 1));
        Assert.Equal((7, 320, 20), (px, py, cy));
    }

    [Fact]
    public void HoverReStylesTheDocumentAndStateChangesNameTheElements()
    {
        InteractivePage p = Ix.Page(
            "<div id=a style='width:100px;height:100px'><div id=b style='width:50px;height:50px'></div></div>",
            extraStyle: "#a:hover{background-color:rgb(0,128,0)}");
        int gen = p.Generation;
        Assert.Equal(InputOutcome.StatesChanged | InputOutcome.Relaid, p.HandlePointer(Ix.Move(10, 10, 1)));
        Assert.Equal(gen + 1, p.Generation);
        Assert.InRange(p.Page.Styled.FindBySource(Ix.Id(p, "a").Index).Values.BackgroundColor.G, 0.49f, 0.51f);
        var names = new List<string>();
        foreach (ElementStateChange c in p.LastStateChanges)
        {
            Assert.Equal(ElementStates.Hover, c.Changed);
            names.Add(Ix.IdOf(c.Element));
        }

        names.Sort(StringComparer.Ordinal);
        Assert.Equal(["a", "b", "body", "html"], names);

        // Moving from b to a changes only b; the page is restyled once more only if a style differs (it does not).
        Assert.Equal(InputOutcome.StatesChanged, p.HandlePointer(Ix.Move(80, 80, 2)));
        Assert.Equal(gen + 1, p.Generation);
        Assert.Single(p.LastStateChanges.ToArray());

        // Leaving removes the hover style again.
        p.HandlePointer(Ix.Move(300, 250, 3));
        Assert.Equal(0f, p.Page.Styled.FindBySource(Ix.Id(p, "a").Index).Values.BackgroundColor.G);
    }

    [Fact]
    public void ActiveStateFollowsThePrimaryButton()
    {
        InteractivePage p = Ix.Page("<div id=a style='width:100px;height:100px'></div>", extraStyle: "#a:active{background-color:rgb(255,0,0)}");
        p.HandlePointer(Ix.Down(10, 10, 1));
        Assert.Equal(Ix.Id(p, "a"), p.ActiveElement);
        Assert.Equal(1f, p.Page.Styled.FindBySource(Ix.Id(p, "a").Index).Values.BackgroundColor.R);
        p.HandlePointer(Ix.Up(10, 10, 2));
        Assert.True(p.ActiveElement.IsNull);
        Assert.Equal(0f, p.Page.Styled.FindBySource(Ix.Id(p, "a").Index).Values.BackgroundColor.R);
    }

    [Fact]
    public void AnInstalledHandlerReceivesTheChangesInsteadOfAFullRestyle()
    {
        InteractivePage p = Ix.Page("<div id=a style='width:100px;height:100px'></div>", extraStyle: "#a:hover{background-color:rgb(0,128,0)}");
        var handler = new RecordingHandler();
        p.StateChangeHandler = handler;
        int gen = p.Generation;
        InputOutcome o = p.HandlePointer(Ix.Move(10, 10, 1));
        Assert.Equal(InputOutcome.StatesChanged, o);
        Assert.Equal(gen, p.Generation);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(Ix.Id(p, "a"), handler.Context!.Hover);
        Assert.Contains("a", handler.Names);
    }

    private sealed class RecordingHandler : IStateChangeHandler
    {
        public int Calls { get; private set; }

        public StyleContext? Context { get; private set; }

        public List<string> Names { get; } = [];

        public bool ElementStatesChanged(InteractivePage page, ReadOnlySpan<ElementStateChange> changes, StyleContext context)
        {
            Calls++;
            Context = context;
            foreach (ElementStateChange c in changes)
            {
                Names.Add(Ix.IdOf(c.Element));
            }

            return true;
        }
    }
}
