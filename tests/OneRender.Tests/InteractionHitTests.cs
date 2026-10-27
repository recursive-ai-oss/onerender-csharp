using System.Text;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Layout;
using RecursiveAI.OneHtml;
using Xunit;

namespace OneRender.Tests;

/// <summary>Shared construction for the interaction tests.</summary>
internal static class Ix
{
    public const string Head = "<!DOCTYPE html><html><head><style>html,body{margin:0}div,p,span,button,a{box-sizing:border-box}</style></head><body>";

    public static InteractivePage Page(string body, int w = 400, int h = 300, string extraStyle = "", InteractionOptions? io = null)
    {
        string html = Head.Replace("</style>", extraStyle + "</style>", StringComparison.Ordinal) + body + "</body></html>";
        return InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = w, ViewportHeight = h }, io);
    }

    public static Node Id(InteractivePage p, string id) => p.Page.Document.GetElementById(id);

    public static string IdOf(Node n) => n.IsNull ? "(none)" : (n.GetAttribute("id") ?? n.LocalName);

    public static string At(InteractivePage p, double x, double y) => IdOf(p.ElementFromPoint(x, y));

    public static PointerInput Move(double x, double y, double t = 0, MouseButtons b = MouseButtons.None) =>
        new() { Action = PointerAction.Move, X = x, Y = y, Time = t, Buttons = b };

    public static PointerInput Down(double x, double y, double t = 0, int button = 0) =>
        new() { Action = PointerAction.Down, X = x, Y = y, Time = t, Button = button, Buttons = (MouseButtons)(1 << button) };

    public static PointerInput Up(double x, double y, double t = 0, int button = 0) =>
        new() { Action = PointerAction.Up, X = x, Y = y, Time = t, Button = button, Buttons = MouseButtons.None };

    public static KeyInput Key(string key, bool down = true, KeyModifiers m = KeyModifiers.None) => new() { Key = key, Down = down, Modifiers = m };

    /// <summary>Records every event of the given types on a target as "type@target[phase]".</summary>
    public static void Record(InteractivePage p, List<string> log, EventTarget target, string label, bool capture, params string[] types)
    {
        foreach (string t in types)
        {
            p.Events.AddEventListener(target, t, e => log.Add(e.Type + "@" + label + (e.Phase == EventPhase.AtTarget ? "*" : string.Empty)), new EventListenerOptions { Capture = capture });
        }
    }
}

public class HitTestingTests
{
    [Fact]
    public void PositionedBoxesStackByZIndexThenTreeOrder()
    {
        InteractivePage p = Ix.Page(
            "<div id=a style='position:absolute;left:0;top:0;width:100px;height:100px;z-index:2'></div>" +
            "<div id=b style='position:absolute;left:50px;top:50px;width:100px;height:100px;z-index:1'></div>" +
            "<div id=c style='position:absolute;left:80px;top:80px;width:100px;height:100px;z-index:1'></div>");
        Assert.Equal("a", Ix.At(p, 60, 60));
        Assert.Equal("a", Ix.At(p, 90, 90));
        Assert.Equal("c", Ix.At(p, 120, 120));
        Assert.Equal("b", Ix.At(p, 60, 120));
        Assert.Equal("html", Ix.At(p, 300, 250));
    }

    [Fact]
    public void PaintOrderPutsPositionedAboveInlineAboveFloatsAboveBlocks()
    {
        // Block 100px tall; a float pulled over it with a negative margin; an inline-block pulled over the float; a positioned box on top.
        InteractivePage p = Ix.Page(
            "<div id=blk style='height:100px'></div>" +
            "<div id=fl style='float:left;width:50px;height:50px;margin-top:-100px'></div>" +
            "<span id=ib style='display:inline-block;width:20px;height:20px;margin-left:-40px;margin-top:0px;vertical-align:top'></span>" +
            "<div id=pos style='position:absolute;left:0;top:0;width:12px;height:12px'></div>");
        Assert.Equal("pos", Ix.At(p, 5, 5));
        Assert.Equal("blk", Ix.At(p, 150, 60));
        Assert.Equal("fl", Ix.At(p, 40, 40));
        string top = Ix.At(p, 15, 15);
        Assert.True(top is "ib" or "fl", top);
    }

    [Fact]
    public void InlineContentPaintsAboveFloatsAndBlockBackgrounds()
    {
        // A float over a block, then text from a later paragraph overlapping both: inline content is above the float.
        InteractivePage p = Ix.Page(
            "<div id=blk style='height:60px'></div>" +
            "<div id=fl style='float:left;width:60px;height:60px;margin-top:-60px'></div>" +
            "<div id=ib2 style='display:inline-block;width:30px;height:30px;margin-left:-60px;vertical-align:top'></div>");
        Assert.Equal("fl", Ix.At(p, 50, 50));
        Assert.Equal("blk", Ix.At(p, 200, 30));
        // The inline-block sits at the float's left edge region.
        Node n = p.ElementFromPoint(10, 10);
        Assert.True(Ix.IdOf(n) is "ib2" or "fl", Ix.IdOf(n));
    }

    [Fact]
    public void NegativeZIndexIsBelowBlockBackgroundsAndPositiveAbove()
    {
        InteractivePage p = Ix.Page(
            "<div id=par style='height:100px;background:#eee'>" +
            "<div id=neg style='position:absolute;left:0;top:0;width:50px;height:50px;z-index:-1'></div>" +
            "<div id=pos style='position:absolute;left:60px;top:0;width:50px;height:50px;z-index:1'></div>" +
            "</div>");
        // The negative box is painted in the root's stacking context before the block, so the block covers it.
        Assert.Equal("par", Ix.At(p, 10, 10));
        Assert.Equal("pos", Ix.At(p, 70, 10));
    }

    [Fact]
    public void OverflowClipsHitsAndScrollOffsetsMoveContent()
    {
        InteractivePage p = Ix.Page(
            "<div id=box style='width:100px;height:100px;overflow:auto'>" +
            "<div id=r1 style='height:100px'></div><div id=r2 style='height:100px'></div><div id=r3 style='height:100px'></div></div>" +
            "<div id=after style='height:400px'></div>");
        Assert.Equal("r1", Ix.At(p, 10, 10));
        Assert.Equal("r1", Ix.At(p, 10, 99));
        // Below the box is the following block, even though r2 overflows there.
        Assert.Equal("after", Ix.At(p, 10, 150));
        Assert.True(p.ScrollElementTo(Ix.Id(p, "box"), 0, 100));
        Assert.Equal("r2", Ix.At(p, 10, 10));
        Assert.True(p.ScrollElementTo(Ix.Id(p, "box"), 0, 1000));
        Assert.Equal(200, p.Layout.ScrollTop(Ix.Id(p, "box")));
        Assert.Equal("r3", Ix.At(p, 10, 99));
    }

    [Fact]
    public void NestedClipsApplyToPositionedDescendantsOnlyThroughTheirContainingBlock()
    {
        // #outer clips, #mid is static and clips, #abs is positioned relative to #outer, so #mid does not clip it.
        InteractivePage p = Ix.Page(
            "<div id=outer style='position:relative;width:200px;height:200px;overflow:hidden'>" +
            "<div id=mid style='width:50px;height:50px;overflow:hidden'>" +
            "<div id=abs style='position:absolute;left:100px;top:100px;width:50px;height:50px'></div></div></div>");
        Assert.Equal("abs", Ix.At(p, 110, 110));
        Assert.Equal("outer", Ix.At(p, 60, 60));
        Assert.Equal("html", Ix.At(p, 250, 250));
    }

    [Fact]
    public void TransformsMoveAndRotateTheHitArea()
    {
        InteractivePage p = Ix.Page(
            "<div id=t style='width:100px;height:100px;transform:translateX(150px)'></div>" +
            "<div id=r style='position:absolute;left:0;top:200px;width:100px;height:20px;transform:rotate(90deg)'></div>");
        // The body is 100px tall (the untransformed place of #t), so where #t no longer is, the body is hit.
        Assert.Equal("body", Ix.At(p, 50, 50));
        Assert.Equal("t", Ix.At(p, 200, 50));
        Assert.Equal("body", Ix.At(p, 300, 50));
        // The 100x20 box rotates about its centre (50,210): afterwards it covers x 40..60, y 160..260.
        Assert.Equal("r", Ix.At(p, 50, 165));
        Assert.Equal("r", Ix.At(p, 50, 255));
        Assert.Equal("html", Ix.At(p, 10, 210));
    }

    [Fact]
    public void ScaleAndPercentTranslateUseTheBorderBox()
    {
        InteractivePage p = Ix.Page("<div id=s style='width:100px;height:100px;margin:50px;transform:scale(0.5);'></div><div id=q style='position:absolute;left:0;top:200px;width:100px;height:40px;transform:translate(100%,0)'></div>");
        // Scaled about the centre (100,100) to 50x50: covers 75..125.
        Assert.Equal("s", Ix.At(p, 100, 100));
        Assert.Equal("body", Ix.At(p, 60, 60));
        Assert.Equal("q", Ix.At(p, 150, 220));
        Assert.Equal("html", Ix.At(p, 50, 220));
    }

    [Fact]
    public void PointerEventsNoneLetsTheHitFallThroughButNotItsDescendants()
    {
        InteractivePage p = Ix.Page(
            "<div id=under style='width:100px;height:100px'></div>" +
            "<div id=ghost style='position:absolute;left:0;top:0;width:100px;height:100px;pointer-events:none'>" +
            "<div id=live style='position:absolute;left:0;top:0;width:30px;height:30px;pointer-events:auto'></div></div>");
        Assert.Equal("under", Ix.At(p, 60, 60));
        Assert.Equal("live", Ix.At(p, 10, 10));
    }

    [Fact]
    public void VisibilityHiddenIsNotHitButVisibleChildrenAre()
    {
        InteractivePage p = Ix.Page(
            "<div id=under style='width:100px;height:100px'></div>" +
            "<div id=hid style='position:absolute;left:0;top:0;width:100px;height:100px;visibility:hidden'>" +
            "<div id=vis style='position:absolute;left:0;top:0;width:30px;height:30px;visibility:visible'></div></div>");
        Assert.Equal("under", Ix.At(p, 60, 60));
        Assert.Equal("vis", Ix.At(p, 10, 10));
    }

    [Fact]
    public void BorderRadiusCutsTheCorners()
    {
        InteractivePage p = Ix.Page("<div id=c style='width:100px;height:100px;border-radius:50px'></div>");
        Assert.Equal("c", Ix.At(p, 50, 50));
        Assert.Equal("c", Ix.At(p, 50, 1));
        Assert.Equal("body", Ix.At(p, 2, 2));
        Assert.Equal("body", Ix.At(p, 98, 98));
    }

    [Fact]
    public void RoundedOverflowClipCutsCornersOfChildren()
    {
        InteractivePage p = Ix.Page("<div id=c style='width:100px;height:100px;border-radius:50px;overflow:hidden'><div id=k style='width:100px;height:100px'></div></div>");
        Assert.Equal("k", Ix.At(p, 50, 50));
        Assert.Equal("body", Ix.At(p, 2, 2));
    }

    [Fact]
    public void PointOutsideTheViewportHitsNothing()
    {
        InteractivePage p = Ix.Page("<div id=a style='width:1000px;height:1000px'></div>", 200, 100);
        Assert.Equal("a", Ix.At(p, 10, 10));
        Assert.True(p.ElementFromPoint(250, 10).IsNull);
        Assert.True(p.ElementFromPoint(10, -1).IsNull);
        Assert.True(p.ElementFromPoint(10, 100).IsNull);
    }

    [Fact]
    public void ViewportScrollShiftsHitsAndFixedBoxesStayPut()
    {
        InteractivePage p = Ix.Page(
            "<div id=top style='height:300px'></div><div id=low style='height:300px'></div>" +
            "<div id=fx style='position:fixed;left:0;top:0;width:20px;height:20px'></div>", 200, 100);
        Assert.Equal("fx", Ix.At(p, 5, 5));
        Assert.Equal("top", Ix.At(p, 50, 50));
        Assert.True(p.ScrollViewportTo(0, 250));
        Assert.Equal("fx", Ix.At(p, 5, 5));
        Assert.Equal("top", Ix.At(p, 50, 20));
        Assert.Equal("low", Ix.At(p, 50, 80));
    }

    [Fact]
    public void ElementsFromPointListsTheWholeStackTopFirst()
    {
        InteractivePage p = Ix.Page("<div id=a style='position:absolute;left:0;top:0;width:100px;height:100px'><div id=b style='position:absolute;left:0;top:0;width:50px;height:50px'></div></div>");
        var nodes = new Node[8];
        int n = p.ElementsFromPoint(10, 10, nodes);
        var ids = new List<string>();
        for (int i = 0; i < n; i++)
        {
            ids.Add(Ix.IdOf(nodes[i]));
        }

        Assert.Equal(["b", "a", "html"], ids);
    }

    [Fact]
    public void TextHitsGiveTheOwningElementAndACaretOffset()
    {
        InteractivePage p = Ix.Page("<p id=p style='margin:0;font:20px Liberation Sans;width:400px'>hello world</p>");
        Assert.True(p.Hits.HitTest(1, 8, out HitResult first));
        Assert.True(first.IsText);
        Assert.Equal("p", Ix.IdOf(first.Node));
        Assert.Equal(0, first.TextOffset);
        Rect line = p.Layout.GetBoundingClientRect(Ix.Id(p, "p"));
        Assert.True(line.Height > 0);
        // Find the right edge of the text by walking right until the paragraph box (not text) is hit.
        int lastOffset = 0;
        double lastX = 0;
        for (double x = 1; x < 300; x += 1)
        {
            if (!p.Hits.HitTest(x, 8, out HitResult h) || !h.IsText)
            {
                break;
            }

            Assert.True(h.TextOffset >= lastOffset, $"offset went back at x={x}");
            lastOffset = h.TextOffset;
            lastX = x;
        }

        Assert.True(lastX > 40);
        Assert.Equal(11, lastOffset);
    }

    [Fact]
    public void DeepNestingNeitherRecursesNorHangs()
    {
        var sb = new StringBuilder();
        const int Depth = 4000;
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("<span id=leaf>x</span>");
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("</div>");
        }

        InteractivePage p = Ix.Page(sb.ToString());
        Assert.False(p.ElementFromPoint(1, 5).IsNull);
        var nodes = new Node[300];
        Assert.True(p.ElementsFromPoint(1, 5, nodes) > 0);
    }
}
