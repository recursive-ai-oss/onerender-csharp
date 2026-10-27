using OneRender.Image;
using OneRender.Interaction;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Transitions and animations driven through an interactive page by the host clock.</summary>
public class MotionPageTests
{
    private static InteractivePage Page(string body, string css, double? start = 0)
    {
        string html = Ix.Head.Replace("</style>", css + "</style>", StringComparison.Ordinal) + body + "</body></html>";
        return InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300, AnimationTime = start });
    }

    private static double Opacity(InteractivePage p, string id) => p.Page.Styled.FindBySource(Ix.Id(p, id).Index).Values.Opacity;

    private static void Log(InteractivePage p, List<string> log, string id, params string[] types)
    {
        foreach (string t in types)
        {
            p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, id)), t, e =>
            {
                string detail = e is TransitionEvent te ? te.PropertyName : e is AnimationEvent ae ? ae.AnimationName : string.Empty;
                log.Add(e.Type + ":" + detail);
            });
        }
    }

    [Fact]
    public void KeyframesInterpolateOverTheHostClock()
    {
        InteractivePage p = Page("<div id=a style='animation:fade 1s linear both'>x</div>", "@keyframes fade{from{opacity:0}to{opacity:1}}");
        Assert.True(p.IsAnimating);
        p.Advance(0);
        Assert.Equal(0, Opacity(p, "a"), 6);
        InputOutcome o = p.Advance(250);
        Assert.True((o & InputOutcome.Animated) != 0);
        Assert.Equal(0.25, Opacity(p, "a"), 6);
        p.Advance(1000);
        Assert.Equal(1, Opacity(p, "a"), 6);
        p.Advance(5000);
        Assert.Equal(1, Opacity(p, "a"), 6);
        Assert.False(p.IsAnimating);
    }

    [Fact]
    public void OpacityAnimationDoesNotRelayout()
    {
        InteractivePage p = Page("<div id=a style='animation:fade 1s linear infinite'>x</div>", "@keyframes fade{from{opacity:0}to{opacity:1}}");
        int gen = p.Generation;
        InputOutcome o = p.Advance(100);
        Assert.Equal(InputOutcome.Animated, o & (InputOutcome.Animated | InputOutcome.Relaid));
        Assert.Equal(gen, p.Generation);
    }

    [Fact]
    public void WidthAnimationRelaysOut()
    {
        InteractivePage p = Page("<div id=a style='height:10px;animation:grow 1s linear both'></div>", "@keyframes grow{from{width:100px}to{width:200px}}");
        p.Advance(500);
        Assert.Equal(150, p.Layout.GetBoundingClientRect(Ix.Id(p, "a")).Width, 3);
    }

    [Fact]
    public void AnimationEventsComeInOrder()
    {
        InteractivePage p = Page("<div id=a style='animation:fade 1s linear 2 both'>x</div>", "@keyframes fade{from{opacity:0}to{opacity:1}}");
        var log = new List<string>();
        Log(p, log, "a", "animationstart", "animationiteration", "animationend", "animationcancel");
        p.Advance(0);
        p.Advance(500);
        p.Advance(1500);
        p.Advance(2500);
        Assert.Equal(["animationstart:fade", "animationiteration:fade", "animationend:fade"], log);
    }

    [Fact]
    public void DelayHoldsTheFirstFrame()
    {
        InteractivePage p = Page("<div id=a style='animation:fade 1s linear 500ms both'>x</div>", "@keyframes fade{from{opacity:0.2}to{opacity:1}}");
        p.Advance(100);
        Assert.Equal(0.2, Opacity(p, "a"), 6);
        p.Advance(1000);
        Assert.Equal(0.2 + (0.8 * 0.5), Opacity(p, "a"), 6);
    }

    [Fact]
    public void HoverStartsATransitionThatRunsToTheEnd()
    {
        InteractivePage p = Page("<div id=a style='width:100px;height:50px'>x</div>", "#a{opacity:0;transition:opacity 1s linear}#a:hover{opacity:1}");
        var log = new List<string>();
        Log(p, log, "a", "transitionrun", "transitionstart", "transitionend", "transitioncancel");
        p.HandlePointer(Ix.Move(10, 10, 100));
        p.Advance(100);
        Assert.Equal(0, Opacity(p, "a"), 6);
        p.Advance(600);
        Assert.Equal(0.5, Opacity(p, "a"), 6);
        p.Advance(1100);
        Assert.Equal(1, Opacity(p, "a"), 6);
        Assert.Equal(["transitionrun:opacity", "transitionstart:opacity", "transitionend:opacity"], log);
    }

    [Fact]
    public void LeavingMidTransitionReversesFromTheCurrentValue()
    {
        InteractivePage p = Page("<div id=a style='width:100px;height:50px'>x</div>", "#a{opacity:0;transition:opacity 1s linear}#a:hover{opacity:1}");
        var log = new List<string>();
        Log(p, log, "a", "transitionrun", "transitionend", "transitioncancel");
        p.HandlePointer(Ix.Move(10, 10, 0));
        p.Advance(400);
        Assert.Equal(0.4, Opacity(p, "a"), 6);
        p.HandlePointer(Ix.Move(300, 250, 400));
        p.Advance(400);
        Assert.Equal(0.4, Opacity(p, "a"), 6);
        p.Advance(600);
        // Reversing shortened duration (Transitions 1 §3.2): 0.4 of the way back takes 0.4 of the duration.
        Assert.Equal(0.2, Opacity(p, "a"), 6);
        p.Advance(900);
        Assert.Equal(0, Opacity(p, "a"), 6);
        Assert.Contains("transitioncancel:opacity", log);
    }

    [Fact]
    public void ZeroDurationTransitionsNeverStart()
    {
        InteractivePage p = Page("<div id=a style='width:100px;height:50px'>x</div>", "#a{opacity:0;transition:opacity 0s}#a:hover{opacity:1}");
        var log = new List<string>();
        Log(p, log, "a", "transitionrun");
        p.HandlePointer(Ix.Move(10, 10, 0));
        p.Advance(10);
        Assert.Equal(1, Opacity(p, "a"), 6);
        Assert.Empty(log);
    }

    [Fact]
    public void ReducedMotionIsAMediaFeature()
    {
        string html = Ix.Head.Replace("</style>", "#a{width:10px}@media (prefers-reduced-motion: reduce){#a{width:20px}}</style>", StringComparison.Ordinal) + "<div id=a></div></body></html>";
        InteractivePage p = InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300, PrefersReducedMotion = true });
        Assert.Equal(20, p.Layout.GetBoundingClientRect(Ix.Id(p, "a")).Width, 3);
    }

    [Fact]
    public void PausedAnimationHoldsItsValue()
    {
        InteractivePage p = Page("<div id=a style='animation:fade 1s linear both paused'>x</div>", "@keyframes fade{from{opacity:0}to{opacity:1}}");
        p.Advance(500);
        Assert.Equal(0, Opacity(p, "a"), 6);
    }

    [Fact]
    public void StaticPagesDoNotAnimate()
    {
        string html = Ix.Head.Replace("</style>", "@keyframes f{from{opacity:0.5}to{opacity:1}}</style>", StringComparison.Ordinal) + "<div id=a style='animation:f 1s linear both'>x</div></body></html>";
        HtmlPage page = HtmlPage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300 });
        Assert.Equal(1, page.Styled.FindBySource(page.Document.GetElementById("a").Index).Values.Opacity, 6);
    }

    private static string ColorOf(InteractivePage p, string id)
    {
        RecursiveAI.OneHtml.Node n = Ix.Id(p, id);
        return OneRender.Style.ComputedValueText.Get(OneRender.Style.PropertyId.Color, in p.Page.Styled.FindBySource(n.Index).Values);
    }

    [Fact]
    public void ATransitionedColorReachesTextAndInheritingDescendants()
    {
        InteractivePage p = Page("<div id=a tabindex=0 style='width:100px;height:50px'>text<span id=s>inner</span></div>", "#a{color:rgb(0 0 0);transition:color 1s linear}#a:focus{color:rgb(200 100 0)}");
        p.Advance(0);
        p.Focus(Ix.Id(p, "a"));
        p.Advance(500);
        Assert.Equal("rgb(100, 50, 0)", ColorOf(p, "a"));
        // The span inherits the animated value; the text node of the div reads its parent's style.
        Assert.Equal("rgb(100, 50, 0)", ColorOf(p, "s"));
        RecursiveAI.OneHtml.Node a = Ix.Id(p, "a");
        OneRender.Tree.StyledNode sa = p.Page.Styled.FindBySource(a.Index);
        for (OneRender.Tree.StyledNode c = sa.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == OneRender.Tree.StyledNodeKind.Text)
            {
                Assert.Equal("rgb(100, 50, 0)", OneRender.Style.ComputedValueText.Get(OneRender.Style.PropertyId.Color, in c.Values));
            }
        }
    }

    [Fact]
    public void ChangingTransitionPropertyCancelsTheRunningTransition()
    {
        InteractivePage p = Page("<div id=a tabindex=0 style='width:100px;height:50px'>x</div>", "#a{opacity:0;transition:opacity 1s linear}#a:focus{opacity:1;transition:none}");
        var log = new List<string>();
        Log(p, log, "a", "transitionrun", "transitionend", "transitioncancel");
        p.Advance(0);
        p.Focus(Ix.Id(p, "a"));
        p.Advance(100);
        Assert.Equal(1, Opacity(p, "a"), 6);
        Assert.Empty(log);
    }

    [Fact]
    public void ARunningTransitionIsCancelledWhenItsPropertyLeavesTheList()
    {
        InteractivePage p = Page("<div id=a tabindex=0 style='width:100px;height:50px'>x</div>", "#a{opacity:0;transition:opacity 1s linear}#a:focus{opacity:0.5;transition:opacity 1s linear}#a:focus:hover{transition:width 1s}");
        var log = new List<string>();
        Log(p, log, "a", "transitionrun", "transitionstart", "transitionend", "transitioncancel");
        p.Advance(0);
        p.Focus(Ix.Id(p, "a"));
        p.Advance(200);
        p.HandlePointer(Ix.Move(10, 10, 200));
        p.Advance(300);
        Assert.Equal(["transitionrun:opacity", "transitionstart:opacity", "transitioncancel:opacity"], log);
    }

    [Fact]
    public void EventsOfOneFrameArriveInScheduledOrder()
    {
        InteractivePage p = Page("<div id=a style='animation:f 1s linear 2'>x</div><div id=b style='animation:f 500ms linear 1'>y</div>", "@keyframes f{from{opacity:0}to{opacity:1}}");
        var log = new List<string>();
        foreach (string id in new[] { "a", "b" })
        {
            string label = id;
            foreach (string t in new[] { "animationstart", "animationiteration", "animationend" })
            {
                p.Events.AddEventListener(EventTarget.Of(Ix.Id(p, id)), t, e => log.Add(label + ":" + e.Type));
            }
        }

        p.Advance(0);
        p.Advance(1200);
        // b ended at 500 ms and a iterated at 1000 ms: a late frame still delivers them in that order.
        Assert.Equal(["a:animationstart", "b:animationstart", "b:animationend", "a:animationiteration"], log);
        p.Advance(2500);
        Assert.Equal("a:animationend", log[^1]);
    }

    [Fact]
    public void PausingAndResumingKeepsTheClockOfTheAnimation()
    {
        InteractivePage p = Page("<div id=a tabindex=0>x</div>", "@keyframes f{from{opacity:0}to{opacity:1}}#a{animation:f 1s linear both}#a:focus{animation-play-state:paused}");
        p.Advance(0);
        p.Advance(300);
        p.Focus(Ix.Id(p, "a"));
        p.Advance(900);
        Assert.Equal(0.3, Opacity(p, "a"), 3);
        p.Blur();
        p.Advance(1000);
        p.Advance(1200);
        Assert.Equal(0.6, Opacity(p, "a"), 3);
    }
}
