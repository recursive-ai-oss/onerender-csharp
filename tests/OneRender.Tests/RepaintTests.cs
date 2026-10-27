using OneRender.Image;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>Painting only what an animation tick changed gives the list a full paint gives, and costs no allocation.</summary>
public class RepaintTests
{
    private const string Css =
        "@keyframes spin{to{transform:rotate(360deg)}}@keyframes grow{from{width:20px}to{width:70px}}" +
        "@keyframes tint{from{color:#ff0000}to{color:#0000ff}}@keyframes bg{from{background-color:#102030}to{background-color:#a0b0c0}}" +
        "#slot3{animation:grow 1s linear infinite alternate}#slot20{animation:grow 700ms ease-in-out infinite alternate}" +
        "#level{animation:tint 1s linear infinite alternate}#quest3{animation:bg 900ms linear infinite alternate}" +
        "#slot5{animation:spin 2s linear infinite}";

    private static InteractivePage Create() =>
        InteractivePage.Create(
            GameUiScene.Html().Replace("</style>", Css + "</style>", StringComparison.Ordinal),
            new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height },
            new InteractionOptions { AnimationStartTime = 0 });

    [Fact]
    public void RepaintedTicksEqualAFullPaintAndMostlyPatch()
    {
        InteractivePage p = Create();
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll };
        p.Repaint(painter, retained, options);
        double t = 0;
        for (int i = 0; i < 400; i++)
        {
            t += 16;
            p.Advance(t);
            p.Repaint(painter, retained, options);
            var full = new DisplayList();
            new Painter().Paint(p.Page.Fragments, full, options);
            DisplayList mine = retained.Current;
            Assert.Equal(full.Count, mine.Count);
            for (int k = 0; k < full.Count; k++)
            {
                Assert.True(full[k].Id == mine[k].Id && full[k].ContentHash == mine[k].ContentHash && full[k].ContextHash == mine[k].ContextHash
                    && full[k].RootBounds == mine[k].RootBounds && full[k].ParentIndex == mine[k].ParentIndex, "tick " + i + " item " + k);
            }
        }

        Assert.True(painter.PatchedPaints > 100, "patched " + painter.PatchedPaints + " of 400 ticks");
    }

    [Fact]
    public void RepaintedTicksAllocateNothingOnceWarm()
    {
        InteractivePage p = Create();
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        var options = new PaintOptions(GameUiScene.Width, GameUiScene.Height) { Scroll = p.Page.Scroll };
        p.Repaint(painter, retained, options);
        double t = 0;

        void Frame()
        {
            t += 16;
            p.Advance(t);
            p.Repaint(painter, retained, options);
        }

        Allocation.AssertZero(Frame, "advance and repaint of animated width and color on the HUD", warmup: 400, iterations: 100);
    }
}
